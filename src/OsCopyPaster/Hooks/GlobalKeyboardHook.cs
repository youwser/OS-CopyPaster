using System.Diagnostics;
using System.Runtime.InteropServices;
using OsCopyPaster.App;
using OsCopyPaster.Config;
using OsCopyPaster.Interop;

namespace OsCopyPaster.Hooks;

/// <summary>
/// Low-level keyboard hook. Listens for the 6 dedicated hotkeys and fires actions instantly.
/// </summary>
internal class GlobalKeyboardHook : IDisposable
{
    private readonly NativeMethods.LowLevelKeyboardProc _hookProc;
    private IntPtr _hookId = IntPtr.Zero;

    private readonly AppState _appState;
    private readonly SynchronizationContext _syncContext;

    // The 6 hotkeys
    private KeyCombo _s1Copy;
    private KeyCombo _s1Paste;
    private KeyCombo _s2Copy;
    private KeyCombo _s2Paste;
    private KeyCombo _s3Copy;
    private KeyCombo _s3Paste;

    public bool IsSimulating { get; set; }

    public event Action<HotkeyAction>? HotkeyPressed;

    public GlobalKeyboardHook(AppState appState, AppConfig config)
    {
        _appState = appState;
        _hookProc = HookCallback;
        _syncContext = SynchronizationContext.Current!;
        
        UpdateKeybinds(config);
    }

    public void Install()
    {
        if (_hookId != IntPtr.Zero) return;

        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule!;
        _hookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _hookProc,
            NativeMethods.GetModuleHandle(module.ModuleName),
            0);
    }

    public void UpdateKeybinds(AppConfig config)
    {
        _s1Copy = KeyCombo.Parse(config.Slot1CopyKey);
        _s1Paste = KeyCombo.Parse(config.Slot1PasteKey);
        
        _s2Copy = KeyCombo.Parse(config.Slot2CopyKey);
        _s2Paste = KeyCombo.Parse(config.Slot2PasteKey);
        
        _s3Copy = KeyCombo.Parse(config.Slot3CopyKey);
        _s3Paste = KeyCombo.Parse(config.Slot3PasteKey);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || IsSimulating || !_appState.IsEnabled)
            return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);

        var msgType = wParam.ToInt32();
        if (msgType != NativeMethods.WM_KEYDOWN && msgType != NativeMethods.WM_SYSKEYDOWN)
            return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);

        var hookStruct = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
        var vkCode = hookStruct.vkCode;

        // Check slots
        if (CheckAndFire(_s1Copy, vkCode, HotkeyType.SecondaryCopy, 1)) return (IntPtr)1;
        if (CheckAndFire(_s1Paste, vkCode, HotkeyType.SecondaryPaste, 1)) return (IntPtr)1;
        
        if (CheckAndFire(_s2Copy, vkCode, HotkeyType.SecondaryCopy, 2)) return (IntPtr)1;
        if (CheckAndFire(_s2Paste, vkCode, HotkeyType.SecondaryPaste, 2)) return (IntPtr)1;
        
        if (CheckAndFire(_s3Copy, vkCode, HotkeyType.SecondaryCopy, 3)) return (IntPtr)1;
        if (CheckAndFire(_s3Paste, vkCode, HotkeyType.SecondaryPaste, 3)) return (IntPtr)1;

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private bool CheckAndFire(KeyCombo combo, uint vkCode, HotkeyType type, int slot)
    {
        if (combo.Matches(vkCode))
        {
            var action = new HotkeyAction(type, slot);
            _syncContext.Post(_ => HotkeyPressed?.Invoke(action), null);
            return true;
        }
        return false;
    }

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
        GC.SuppressFinalize(this);
    }
}
