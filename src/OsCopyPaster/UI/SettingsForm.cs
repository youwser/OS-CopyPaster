using OsCopyPaster.Config;
using OsCopyPaster.Hooks;

namespace OsCopyPaster.UI;

internal class SettingsForm : Form
{
    private readonly AppConfig _config;
    private readonly ConfigService _configService;

    private readonly NumericUpDown _swapDelayField;
    private readonly CheckBox _notificationsCheck;

    private TextBox? _activeCapture;

    // We store the TextBoxes and the Action to apply them in a list
    private readonly List<(TextBox box, Action<AppConfig, string> apply)> _keybindMap = new();

    public SettingsForm(AppConfig config, ConfigService configService)
    {
        _config = config;
        _configService = configService;

        Text = "OS-CopyPaster Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(380, 360);
        Font = new Font("Segoe UI", 9f);
        KeyPreview = true;
        KeyDown += OnFormKeyDown;

        int y = 20;

        AddKeybindRow("Slot 1 Copy:", config.Slot1CopyKey, (c, v) => c.Slot1CopyKey = v, ref y);
        AddKeybindRow("Slot 1 Paste:", config.Slot1PasteKey, (c, v) => c.Slot1PasteKey = v, ref y);
        y += 10;
        AddKeybindRow("Slot 2 Copy:", config.Slot2CopyKey, (c, v) => c.Slot2CopyKey = v, ref y);
        AddKeybindRow("Slot 2 Paste:", config.Slot2PasteKey, (c, v) => c.Slot2PasteKey = v, ref y);
        y += 10;
        AddKeybindRow("Slot 3 Copy:", config.Slot3CopyKey, (c, v) => c.Slot3CopyKey = v, ref y);
        AddKeybindRow("Slot 3 Paste:", config.Slot3PasteKey, (c, v) => c.Slot3PasteKey = v, ref y);
        y += 10;

        var labelDelay = new Label { Text = "Swap Delay (ms):", Location = new Point(20, y), AutoSize = true };
        _swapDelayField = new NumericUpDown
        {
            Location = new Point(160, y - 3),
            Width = 100, Minimum = 50, Maximum = 1000, Increment = 50,
            Value = config.SwapDelayMs
        };
        Controls.Add(labelDelay); Controls.Add(_swapDelayField);
        y += 30;

        _notificationsCheck = new CheckBox
        {
            Text = "Show notifications", Location = new Point(20, y), AutoSize = true,
            Checked = config.ShowNotifications
        };
        Controls.Add(_notificationsCheck);
        y += 40;

        var saveBtn = new Button { Text = "Save", Location = new Point(180, y), Width = 80 };
        saveBtn.Click += OnSave;
        var cancelBtn = new Button { Text = "Cancel", Location = new Point(270, y), Width = 80 };
        cancelBtn.Click += (_, _) => Close();

        Controls.Add(saveBtn); Controls.Add(cancelBtn);
        AcceptButton = saveBtn; CancelButton = cancelBtn;
    }

    private void AddKeybindRow(string label, string initialValue, Action<AppConfig, string> apply, ref int y)
    {
        var lbl = new Label { Text = label, Location = new Point(20, y + 3), AutoSize = true };
        var box = new TextBox
        {
            Location = new Point(160, y), Width = 190, ReadOnly = true,
            Text = KeyCombo.Parse(initialValue).ToDisplayString(), Cursor = Cursors.Hand,
            Tag = initialValue
        };
        box.Click += (_, _) => StartCapture(box);

        Controls.Add(lbl); Controls.Add(box);
        _keybindMap.Add((box, apply));
        y += 30;
    }

    private void StartCapture(TextBox field)
    {
        if (_activeCapture != null)
            _activeCapture.Text = KeyCombo.Parse((string)_activeCapture.Tag!).ToDisplayString();

        _activeCapture = field;
        field.Text = "Press keys...";
    }

    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        if (_activeCapture == null) return;
        e.Handled = true; e.SuppressKeyPress = true;
        if (e.KeyCode == Keys.Escape)
        {
            _activeCapture.Text = KeyCombo.Parse((string)_activeCapture.Tag!).ToDisplayString();
            _activeCapture = null;
            return;
        }
        if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LControlKey or Keys.RControlKey or Keys.LShiftKey or Keys.RShiftKey or Keys.LMenu or Keys.RMenu)
            return;

        var combo = new KeyCombo(e.Modifiers, e.KeyCode);
        _activeCapture.Tag = combo.ToString();
        _activeCapture.Text = combo.ToDisplayString();
        _activeCapture = null;
    }

    private void OnSave(object? sender, EventArgs e)
    {
        foreach (var (box, apply) in _keybindMap)
            apply(_config, (string)box.Tag!);

        _config.SwapDelayMs = (int)_swapDelayField.Value;
        _config.ShowNotifications = _notificationsCheck.Checked;
        _configService.Save(_config);
        Close();
    }
}
