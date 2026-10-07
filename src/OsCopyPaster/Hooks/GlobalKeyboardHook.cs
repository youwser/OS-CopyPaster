using System.Diagnostics;
using System.Runtime.InteropServices;
using OsCopyPaster.App;
using OsCopyPaster.Interop;

namespace OsCopyPaster.Hooks;

/// <summary>
/// Low-level keyboard hook with a two-key chord state machine.
/// 
/// States:
///   Idle → (copy combo) → AwaitingCopySlot → (digit 0-9) → fire SecondaryCopy → Idle
///   Idle → (paste combo) → AwaitingPasteSlot → (digit 0-9) → fire SecondaryPaste → Idle
/// 
/// If no digit is pressed within the chord timeout, defaults to slot 1.
/// If a non-digit key is pressed while awaiting, the chord is cancelled and the key passes through.
/// </summary>
internal class GlobalKeyboardHook : IDisposable
{
    // Store the delegate in a field so the GC doesn't collect it
    // while the native hook still holds a pointer to it.
    private readonly NativeMethods.LowLevelKeyboardProc _hookProc;
    private IntPtr _hookId = IntPtr.Zero;

    private readonly AppState _appState;
    private readonly SynchronizationContext _syncContext;
    private readonly System.Windows.Forms.Timer _chordTimer;

    private KeyCombo _copyCombo;
    private KeyCombo _pasteCombo;

    private ChordState _chordState = ChordState.Idle;

    /// <summary>
    /// Re-entrancy guard. Set to true by the orchestrator before calling SendInput,
    /// so the hook passes simulated keystrokes through without processing.
    /// </summary>
    public bool IsSimulating { get; set; }

    /// <summary>
    /// Fired when a complete hotkey chord is resolved (combo + slot digit or timeout).
    /// Always fired on the STA/UI thread via SynchronizationContext.Post.
    /// </summary>
    public event Action<HotkeyAction>? HotkeyPressed;

    private enum ChordState
    {
        Idle,
        AwaitingCopySlot,
        AwaitingPasteSlot
    }

    public GlobalKeyboardHook(
        AppState appState,
        KeyCombo copyCombo,
        KeyCombo pasteCombo,
        int chordTimeoutMs)
    {
        _appState = appState;
        _copyCombo = copyCombo;
        _pasteCombo = pasteCombo;
        _hookProc = HookCallback;

        _syncContext = SynchronizationContext.Current
            ?? throw new InvalidOperationException(
                "GlobalKeyboardHook must be created on a thread with a SynchronizationContext "
                + "(call after WindowsFormsSynchronizationContext is installed).");

        _chordTimer = new System.Windows.Forms.Timer { Interval = chordTimeoutMs };
        _chordTimer.Tick += OnChordTimeout;
    }

    /// <summary>Installs the low-level keyboard hook.</summary>
    public void Install()
    {
        if (_hookId != IntPtr.Zero)
            return; // already installed

        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule!;

        _hookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _hookProc,
            NativeMethods.GetModuleHandle(module.ModuleName),
            0);

        if (_hookId == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"Failed to install keyboard hook. Win32 error: {error}");
        }
    }

    /// <summary>
    /// Updates keybinds and chord timeout at runtime (called when config changes).
    /// Resets any in-progress chord.
    /// </summary>
    public void UpdateKeybinds(KeyCombo copyCombo, KeyCombo pasteCombo, int chordTimeoutMs)
    {
        _copyCombo = copyCombo;
        _pasteCombo = pasteCombo;
        _chordTimer.Interval = chordTimeoutMs;
        ResetChordState();
    }

    // ── Hook Callback ───────────────────────────────────────────────

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // Standard: if nCode < 0, pass through unconditionally
        if (nCode < 0)
            return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);

        // Re-entrancy guard: pass through our own simulated keystrokes
        if (IsSimulating)
            return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);

        // App disabled: pass through
        if (!_appState.IsEnabled)
            return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);

        // Only process key-down events
        var msgType = wParam.ToInt32();
        if (msgType != NativeMethods.WM_KEYDOWN && msgType != NativeMethods.WM_SYSKEYDOWN)
            return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);

        var hookStruct = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
        var vkCode = hookStruct.vkCode;

        switch (_chordState)
        {
            case ChordState.Idle:
                return HandleIdleState(nCode, wParam, lParam, vkCode);

            case ChordState.AwaitingCopySlot:
                return HandleAwaitingSlot(nCode, wParam, lParam, vkCode, HotkeyType.SecondaryCopy);

            case ChordState.AwaitingPasteSlot:
                return HandleAwaitingSlot(nCode, wParam, lParam, vkCode, HotkeyType.SecondaryPaste);
        }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private IntPtr HandleIdleState(int nCode, IntPtr wParam, IntPtr lParam, uint vkCode)
    {
        if (_copyCombo.Matches(vkCode))
        {
            _chordState = ChordState.AwaitingCopySlot;
            _chordTimer.Start();
            return (IntPtr)1; // suppress the key
        }

        if (_pasteCombo.Matches(vkCode))
        {
            _chordState = ChordState.AwaitingPasteSlot;
            _chordTimer.Start();
            return (IntPtr)1; // suppress the key
        }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private IntPtr HandleAwaitingSlot(
        int nCode, IntPtr wParam, IntPtr lParam,
        uint vkCode, HotkeyType type)
    {
        _chordTimer.Stop();

        int? digit = GetDigitFromVkCode(vkCode);
        if (digit.HasValue)
        {
            // Digit pressed — fire the action for the selected slot
            _chordState = ChordState.Idle;
            var action = new HotkeyAction(type, digit.Value);
            _syncContext.Post(_ => HotkeyPressed?.Invoke(action), null);
            return (IntPtr)1; // suppress the digit key
        }

        // Non-digit pressed — cancel the chord and let the key pass through
        ResetChordState();
        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    // ── Chord Timeout ───────────────────────────────────────────────

    /// <summary>
    /// Timer fires on the STA thread when the user doesn't press a digit
    /// within the timeout window. Defaults to slot 1.
    /// </summary>
    private void OnChordTimeout(object? sender, EventArgs e)
    {
        _chordTimer.Stop();

        var type = _chordState == ChordState.AwaitingCopySlot
            ? HotkeyType.SecondaryCopy
            : HotkeyType.SecondaryPaste;

        _chordState = ChordState.Idle;

        // Default to slot 1 on timeout
        HotkeyPressed?.Invoke(new HotkeyAction(type, 1));
    }

    // ── Helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Maps a virtual key code to a digit 0-9. Supports both top-row and numpad keys.
    /// </summary>
    private static int? GetDigitFromVkCode(uint vkCode)
    {
        // Top row: VK_0 (0x30) through VK_9 (0x39)
        if (vkCode >= 0x30 && vkCode <= 0x39)
            return (int)(vkCode - 0x30);

        // Numpad: VK_NUMPAD0 (0x60) through VK_NUMPAD9 (0x69)
        if (vkCode >= 0x60 && vkCode <= 0x69)
            return (int)(vkCode - 0x60);

        return null;
    }

    private void ResetChordState()
    {
        _chordTimer.Stop();
        _chordState = ChordState.Idle;
    }

    // ── Disposal ────────────────────────────────────────────────────

    public void Dispose()
    {
        _chordTimer.Stop();
        _chordTimer.Dispose();

        if (_hookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }

        GC.SuppressFinalize(this);
    }
}
