using OsCopyPaster.App;
using OsCopyPaster.Clipboard;
using OsCopyPaster.Config;
using OsCopyPaster.Hooks;

namespace OsCopyPaster;

/// <summary>
/// Composition root. Wires all dependencies and starts the WinForms message loop.
/// The [STAThread] attribute ensures the main thread is STA, which is required for:
///   - Clipboard operations (System.Windows.Forms.Clipboard)
///   - NotifyIcon (system tray)
///   - The message pump that drives the low-level keyboard hook
/// </summary>
internal static class Program
{
    [STAThread]
    static void Main()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            File.WriteAllText("crash.log", e.ExceptionObject.ToString());
        };
        Application.ThreadException += (s, e) =>
        {
            File.WriteAllText("crash.log", e.Exception.ToString());
        };

        // Prevent multiple instances
        using var mutex = new Mutex(true, "OsCopyPaster_SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show(
                "OS-CopyPaster is already running.\nCheck the system tray.",
                "OS-CopyPaster",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Ensure the WinForms SynchronizationContext is installed before
        // creating the keyboard hook (which captures it in its constructor).
        // Creating any Control forces installation.
        using var syncContextHelper = new Control();
        syncContextHelper.CreateControl();

        // ── Load Config ─────────────────────────────────────────────
        var configService = new ConfigService();
        var config = configService.Load();

        // ── Create App State ────────────────────────────────────────
        var appState = new AppState();

        // ── Create Keyboard Hook ────────────────────────────────────
        var keyboardHook = new GlobalKeyboardHook(appState, config);

        // ── Create Clipboard Swap Orchestrator ──────────────────────
        var orchestrator = new ClipboardSwapOrchestrator(
            keyboardHook, config.SwapDelayMs);

        // ── Run ─────────────────────────────────────────────────────
        Application.Run(new TrayApplication(
            appState, keyboardHook, orchestrator, configService, config));
    }
}
