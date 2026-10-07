using System.Runtime.InteropServices;
using OsCopyPaster.Interop;

namespace OsCopyPaster.Input;

/// <summary>
/// Simulates Ctrl+C and Ctrl+V keyboard input via SendInput.
/// Handles the subtlety of the user's physical Ctrl key already being held down:
/// sends a synthetic Ctrl-up first, then the full combo, all in a single atomic batch.
/// </summary>
internal static class InputSimulator
{
    /// <summary>Simulates Ctrl+C (copy) in the currently focused application.</summary>
    public static void SendCtrlC()
    {
        SendKeyCombo((ushort)Keys.C);
    }

    /// <summary>Simulates Ctrl+V (paste) in the currently focused application.</summary>
    public static void SendCtrlV()
    {
        SendKeyCombo((ushort)Keys.V);
    }

    /// <summary>
    /// Sends a full Ctrl+&lt;key&gt; sequence as a single atomic SendInput batch.
    /// Releases any physically held Ctrl first to avoid double-modifier issues.
    /// </summary>
    private static void SendKeyCombo(ushort targetKey)
    {
        var inputs = new NativeMethods.INPUT[]
        {
            // 1. Release Ctrl (undo user's physical hold)
            MakeKeyInput((ushort)Keys.ControlKey, NativeMethods.KEYEVENTF_KEYUP),
            // 2. Press Ctrl
            MakeKeyInput((ushort)Keys.ControlKey, 0),
            // 3. Press target key (C or V)
            MakeKeyInput(targetKey, 0),
            // 4. Release target key
            MakeKeyInput(targetKey, NativeMethods.KEYEVENTF_KEYUP),
            // 5. Release Ctrl
            MakeKeyInput((ushort)Keys.ControlKey, NativeMethods.KEYEVENTF_KEYUP),
        };

        NativeMethods.SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static NativeMethods.INPUT MakeKeyInput(ushort vk, uint flags)
    {
        return new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            u = new NativeMethods.INPUTUNION
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
    }
}
