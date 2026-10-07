using OsCopyPaster.Hooks;
using OsCopyPaster.Input;

namespace OsCopyPaster.Clipboard;

/// <summary>
/// Orchestrates the full secondary copy/paste swap sequences:
///   backup clipboard → clear/set → simulate Ctrl+C/V → wait → read/restore.
/// 
/// Manages 10 independent text slots (0-9).
/// Uses _isBusy guard to prevent overlapping operations from rapid-fire hotkeys.
/// </summary>
internal class ClipboardSwapOrchestrator
{
    private readonly Dictionary<int, string?> _slots = new();
    private readonly GlobalKeyboardHook _hook;
    private int _swapDelayMs;
    private bool _isBusy;

    /// <summary>
    /// Fired after a swap operation completes.
    /// Parameters: (HotkeyType type, int slot, bool success)
    /// </summary>
    public event Action<HotkeyType, int, bool>? SwapCompleted;

    public ClipboardSwapOrchestrator(GlobalKeyboardHook hook, int swapDelayMs)
    {
        _hook = hook;
        _swapDelayMs = swapDelayMs;
    }

    /// <summary>Updates the swap delay at runtime (called when config changes).</summary>
    public void UpdateSwapDelay(int delayMs) => _swapDelayMs = delayMs;

    /// <summary>
    /// Entry point for hotkey events. Routes to the appropriate swap sequence.
    /// Uses async void because this is a fire-and-forget event handler;
    /// exceptions are caught internally.
    /// </summary>
    public async void HandleHotkey(HotkeyAction action)
    {
        if (_isBusy)
            return;

        switch (action.Type)
        {
            case HotkeyType.SecondaryCopy:
                await PerformSecondaryCopy(action.Slot);
                break;
            case HotkeyType.SecondaryPaste:
                await PerformSecondaryPaste(action.Slot);
                break;
        }
    }

    /// <summary>
    /// Secondary Copy flow:
    /// 1. Save user's clipboard
    /// 2. Clear clipboard
    /// 3. Simulate Ctrl+C (target app writes selection to clipboard)
    /// 4. Wait for target app to process
    /// 5. Read new clipboard text → store in slot
    /// 6. Restore user's original clipboard
    /// </summary>
    private async Task PerformSecondaryCopy(int slot)
    {
        _isBusy = true;
        try
        {
            _hook.IsSimulating = true;

            // 1. Backup the current clipboard
            var backup = ClipboardManager.Save();

            // 2. Clear so we can detect new content
            ClipboardManager.Clear();

            // 3. Simulate Ctrl+C in the active application
            InputSimulator.SendCtrlC();

            // 4. Wait for the target app to write to the clipboard
            await Task.Delay(_swapDelayMs);

            // 5. Read what was copied
            var copiedText = ClipboardManager.GetText();
            _slots[slot] = copiedText;

            // 6. Restore the user's original clipboard
            ClipboardManager.Restore(backup);

            _hook.IsSimulating = false;
            SwapCompleted?.Invoke(HotkeyType.SecondaryCopy, slot, copiedText != null);
        }
        catch
        {
            _hook.IsSimulating = false;
            SwapCompleted?.Invoke(HotkeyType.SecondaryCopy, slot, false);
        }
        finally
        {
            _isBusy = false;
        }
    }

    /// <summary>
    /// Secondary Paste flow:
    /// 1. Check slot has content
    /// 2. Save user's clipboard
    /// 3. Set clipboard to slot content
    /// 4. Simulate Ctrl+V (target app reads from clipboard)
    /// 5. Wait for target app to process
    /// 6. Restore user's original clipboard
    /// </summary>
    private async Task PerformSecondaryPaste(int slot)
    {
        if (!_slots.TryGetValue(slot, out var text) || text == null)
        {
            SwapCompleted?.Invoke(HotkeyType.SecondaryPaste, slot, false);
            return;
        }

        _isBusy = true;
        try
        {
            _hook.IsSimulating = true;

            // 1. Backup the current clipboard
            var backup = ClipboardManager.Save();

            // 2. Set clipboard to the secondary buffer content
            ClipboardManager.SetText(text);

            // 3. Simulate Ctrl+V in the active application
            InputSimulator.SendCtrlV();

            // 4. Wait for the target app to read from the clipboard
            await Task.Delay(_swapDelayMs);

            // 5. Restore the user's original clipboard
            ClipboardManager.Restore(backup);

            _hook.IsSimulating = false;
            SwapCompleted?.Invoke(HotkeyType.SecondaryPaste, slot, true);
        }
        catch
        {
            _hook.IsSimulating = false;
            SwapCompleted?.Invoke(HotkeyType.SecondaryPaste, slot, false);
        }
        finally
        {
            _isBusy = false;
        }
    }
}
