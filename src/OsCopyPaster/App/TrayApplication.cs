using System.Drawing;
using System.Drawing.Drawing2D;
using OsCopyPaster.Clipboard;
using OsCopyPaster.Config;
using OsCopyPaster.Hooks;
using OsCopyPaster.UI;

namespace OsCopyPaster.App;

/// <summary>
/// Application context that manages the system tray icon, context menu, and app lifecycle.
/// Wires the keyboard hook to the clipboard swap orchestrator and shows balloon notifications.
/// </summary>
internal class TrayApplication : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly GlobalKeyboardHook _keyboardHook;
    private readonly ClipboardSwapOrchestrator _orchestrator;
    private readonly AppState _appState;
    private readonly ConfigService _configService;
    private readonly AppConfig _config;
    private readonly ToolStripMenuItem _toggleItem;

    public TrayApplication(
        AppState appState,
        GlobalKeyboardHook keyboardHook,
        ClipboardSwapOrchestrator orchestrator,
        ConfigService configService,
        AppConfig config)
    {
        _appState = appState;
        _keyboardHook = keyboardHook;
        _orchestrator = orchestrator;
        _configService = configService;
        _config = config;

        // ── System Tray Icon ────────────────────────────────────────
        _notifyIcon = new NotifyIcon
        {
            Icon = CreateTrayIcon(),
            Text = "OS-CopyPaster (Active)",
            Visible = true,
            ContextMenuStrip = BuildContextMenu()
        };

        _toggleItem = (ToolStripMenuItem)_notifyIcon.ContextMenuStrip.Items[0];

        // ── Wire Events ─────────────────────────────────────────────
        _keyboardHook.HotkeyPressed += _orchestrator.HandleHotkey;
        _orchestrator.SwapCompleted += OnSwapCompleted;
        _configService.ConfigChanged += OnConfigChanged;
        _appState.OnToggled += OnAppToggled;

        // ── Install Hook ────────────────────────────────────────────
        _keyboardHook.Install();
    }

    // ── Context Menu ────────────────────────────────────────────────

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        var toggleItem = new ToolStripMenuItem("Enabled")
        {
            Checked = true,
            CheckOnClick = true
        };
        toggleItem.Click += OnToggle;

        var settingsItem = new ToolStripMenuItem("Settings");
        settingsItem.Click += OnSettings;

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += OnExit;

        menu.Items.Add(toggleItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        return menu;
    }

    // ── Tray Icon Generation ────────────────────────────────────────

    /// <summary>
    /// Generates a 16×16 tray icon at runtime: blue rounded rectangle with white "2" text.
    /// </summary>
    private static Icon CreateTrayIcon()
    {
        var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // Blue background
            using var bgBrush = new SolidBrush(Color.FromArgb(50, 110, 200));
            using var path = CreateRoundedRect(0, 0, 15, 15, 3);
            g.FillPath(bgBrush, path);

            // White "2" text (represents "secondary clipboard")
            using var font = new Font("Segoe UI", 9f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(Color.White);
            var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            g.DrawString("2", font, textBrush, new RectangleF(0, 0, 16, 16), sf);
        }

        var hIcon = bmp.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    private static GraphicsPath CreateRoundedRect(int x, int y, int w, int h, int r)
    {
        var path = new GraphicsPath();
        path.AddArc(x, y, r * 2, r * 2, 180, 90);
        path.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
        path.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
        path.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
        path.CloseFigure();
        return path;
    }

    // ── Event Handlers ──────────────────────────────────────────────

    private void OnToggle(object? sender, EventArgs e)
    {
        _appState.IsEnabled = _toggleItem.Checked;
    }

    private void OnAppToggled(bool isEnabled)
    {
        _toggleItem.Checked = isEnabled;
        _notifyIcon.Text = isEnabled
            ? "OS-CopyPaster (Active)"
            : "OS-CopyPaster (Disabled)";
    }

    private void OnSettings(object? sender, EventArgs e)
    {
        using var form = new SettingsForm(_config, _configService);
        form.ShowDialog();
    }

    private void OnExit(object? sender, EventArgs e)
    {
        _notifyIcon.Visible = false;
        _keyboardHook.Dispose();
        Application.Exit();
    }

    /// <summary>
    /// Shows a balloon notification after a swap operation completes.
    /// </summary>
    private void OnSwapCompleted(HotkeyType type, int slot, bool success)
    {
        if (!_config.ShowNotifications)
            return;

        string message = (type, success) switch
        {
            (HotkeyType.SecondaryCopy, true) => $"Copied to slot {slot} \U0001f4cb",
            (HotkeyType.SecondaryCopy, false) => $"Copy to slot {slot} failed",
            (HotkeyType.SecondaryPaste, true) => $"Pasted from slot {slot} \U0001f4cb",
            (HotkeyType.SecondaryPaste, false) => $"Slot {slot} is empty",
            _ => string.Empty
        };

        if (!string.IsNullOrEmpty(message))
        {
            _notifyIcon.ShowBalloonTip(
                timeout: 1000,
                tipTitle: "OS-CopyPaster",
                tipText: message,
                tipIcon: ToolTipIcon.None);
        }
    }

    /// <summary>
    /// Reloads keybinds and swap delay when the config file is saved.
    /// </summary>
    private void OnConfigChanged(AppConfig newConfig)
    {
        var copyCombo = KeyCombo.Parse(newConfig.SecondaryCopyKey);
        var pasteCombo = KeyCombo.Parse(newConfig.SecondaryPasteKey);
        _keyboardHook.UpdateKeybinds(copyCombo, pasteCombo, newConfig.ChordTimeoutMs);
        _orchestrator.UpdateSwapDelay(newConfig.SwapDelayMs);
    }

    // ── Disposal ────────────────────────────────────────────────────

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _keyboardHook.Dispose();
        }

        base.Dispose(disposing);
    }
}
