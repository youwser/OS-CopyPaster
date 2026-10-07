using OsCopyPaster.Config;
using OsCopyPaster.Hooks;

namespace OsCopyPaster.UI;

/// <summary>
/// WinForms settings dialog for configuring keybinds, swap delay, chord timeout,
/// and notification preferences. Keybind fields support click-to-capture mode.
/// </summary>
internal class SettingsForm : Form
{
    private readonly AppConfig _config;
    private readonly ConfigService _configService;

    private readonly TextBox _copyKeyField;
    private readonly TextBox _pasteKeyField;
    private readonly NumericUpDown _swapDelayField;
    private readonly NumericUpDown _chordTimeoutField;
    private readonly CheckBox _notificationsCheck;

    private KeyCombo? _pendingCopyCombo;
    private KeyCombo? _pendingPasteCombo;
    private TextBox? _activeCapture;

    public SettingsForm(AppConfig config, ConfigService configService)
    {
        _config = config;
        _configService = configService;

        // ── Form properties ─────────────────────────────────────────
        Text = "OS-CopyPaster Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(400, 280);
        Font = new Font("Segoe UI", 9f);
        KeyPreview = true;
        KeyDown += OnFormKeyDown;

        // ── Copy Hotkey ─────────────────────────────────────────────
        var labelCopy = new Label
        {
            Text = "Copy Hotkey:",
            Location = new Point(24, 28),
            AutoSize = true
        };

        _copyKeyField = new TextBox
        {
            Location = new Point(160, 25),
            Width = 210,
            ReadOnly = true,
            Text = KeyCombo.Parse(config.SecondaryCopyKey).ToDisplayString(),
            Cursor = Cursors.Hand
        };
        _copyKeyField.Click += (_, _) => StartCapture(_copyKeyField);

        // ── Paste Hotkey ────────────────────────────────────────────
        var labelPaste = new Label
        {
            Text = "Paste Hotkey:",
            Location = new Point(24, 68),
            AutoSize = true
        };

        _pasteKeyField = new TextBox
        {
            Location = new Point(160, 65),
            Width = 210,
            ReadOnly = true,
            Text = KeyCombo.Parse(config.SecondaryPasteKey).ToDisplayString(),
            Cursor = Cursors.Hand
        };
        _pasteKeyField.Click += (_, _) => StartCapture(_pasteKeyField);

        // ── Swap Delay ──────────────────────────────────────────────
        var labelDelay = new Label
        {
            Text = "Swap Delay (ms):",
            Location = new Point(24, 110),
            AutoSize = true
        };

        _swapDelayField = new NumericUpDown
        {
            Location = new Point(160, 107),
            Width = 100,
            Minimum = 50,
            Maximum = 500,
            Increment = 10,
            Value = config.SwapDelayMs
        };

        // ── Chord Timeout ───────────────────────────────────────────
        var labelChord = new Label
        {
            Text = "Chord Timeout (ms):",
            Location = new Point(24, 150),
            AutoSize = true
        };

        _chordTimeoutField = new NumericUpDown
        {
            Location = new Point(160, 147),
            Width = 100,
            Minimum = 500,
            Maximum = 3000,
            Increment = 100,
            Value = config.ChordTimeoutMs
        };

        // ── Notifications ───────────────────────────────────────────
        _notificationsCheck = new CheckBox
        {
            Text = "Show notifications",
            Location = new Point(24, 192),
            AutoSize = true,
            Checked = config.ShowNotifications
        };

        // ── Buttons ─────────────────────────────────────────────────
        var saveButton = new Button
        {
            Text = "Save",
            Location = new Point(195, 238),
            Width = 85
        };
        saveButton.Click += OnSave;

        var cancelButton = new Button
        {
            Text = "Cancel",
            Location = new Point(286, 238),
            Width = 85
        };
        cancelButton.Click += (_, _) => Close();

        // ── Info label ──────────────────────────────────────────────
        var infoLabel = new Label
        {
            Text = "Click a hotkey field, then press your desired key combination. Press Esc to cancel.",
            Location = new Point(24, 260),
            AutoSize = false,
            Size = new Size(360, 18),
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 7.5f)
        };

        // ── Add all controls ────────────────────────────────────────
        Controls.AddRange(new Control[]
        {
            labelCopy, _copyKeyField,
            labelPaste, _pasteKeyField,
            labelDelay, _swapDelayField,
            labelChord, _chordTimeoutField,
            _notificationsCheck,
            saveButton, cancelButton,
            infoLabel
        });

        AcceptButton = saveButton;
        CancelButton = cancelButton;
    }

    // ── Keybind Capture ─────────────────────────────────────────────

    private void StartCapture(TextBox field)
    {
        // Cancel any previous capture
        if (_activeCapture != null)
            StopCapture(restoreOriginal: true);

        _activeCapture = field;
        field.Text = "Press keys…";
        field.BackColor = Color.LightYellow;
    }

    private void StopCapture(bool restoreOriginal)
    {
        if (_activeCapture == null) return;

        _activeCapture.BackColor = SystemColors.Window;

        if (restoreOriginal)
        {
            _activeCapture.Text = _activeCapture == _copyKeyField
                ? KeyCombo.Parse(_config.SecondaryCopyKey).ToDisplayString()
                : KeyCombo.Parse(_config.SecondaryPasteKey).ToDisplayString();
        }

        _activeCapture = null;
    }

    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        if (_activeCapture == null)
            return;

        e.Handled = true;
        e.SuppressKeyPress = true;

        // Escape cancels capture
        if (e.KeyCode == Keys.Escape)
        {
            StopCapture(restoreOriginal: true);
            return;
        }

        // Ignore lone modifier key presses — wait for the full combo
        if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu
            or Keys.LControlKey or Keys.RControlKey
            or Keys.LShiftKey or Keys.RShiftKey
            or Keys.LMenu or Keys.RMenu)
        {
            return;
        }

        // Build the combo from the event
        var combo = new KeyCombo(e.Modifiers, e.KeyCode);

        if (_activeCapture == _copyKeyField)
            _pendingCopyCombo = combo;
        else
            _pendingPasteCombo = combo;

        _activeCapture.Text = combo.ToDisplayString();
        _activeCapture.BackColor = SystemColors.Window;
        _activeCapture = null;
    }

    // ── Save ────────────────────────────────────────────────────────

    private void OnSave(object? sender, EventArgs e)
    {
        if (_pendingCopyCombo.HasValue)
            _config.SecondaryCopyKey = _pendingCopyCombo.Value.ToString();

        if (_pendingPasteCombo.HasValue)
            _config.SecondaryPasteKey = _pendingPasteCombo.Value.ToString();

        _config.SwapDelayMs = (int)_swapDelayField.Value;
        _config.ChordTimeoutMs = (int)_chordTimeoutField.Value;
        _config.ShowNotifications = _notificationsCheck.Checked;

        _configService.Save(_config);
        Close();
    }
}
