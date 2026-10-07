using System.Windows.Forms;

namespace OsCopyPaster.Input;

internal static class InputSimulator
{
    public static void SendCtrlC()
    {
        // SendKeys manages physical modifier key states automatically and is much 
        // more robust for Ctrl+C/V automation than raw SendInput arrays.
        SendKeys.SendWait("^{c}");
    }

    public static void SendCtrlV()
    {
        SendKeys.SendWait("^{v}");
    }
}
