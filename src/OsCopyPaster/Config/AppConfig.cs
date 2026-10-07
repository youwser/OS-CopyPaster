namespace OsCopyPaster.Config;

/// <summary>
/// POCO model for config.json. Defines the 3 dedicated slots.
/// </summary>
public class AppConfig
{
    // Slot 1
    public string Slot1CopyKey { get; set; } = "Ctrl+OemSemicolon"; // Ctrl+;
    public string Slot1PasteKey { get; set; } = "Ctrl+OemQuotes"; // Ctrl+'

    // Slot 2
    public string Slot2CopyKey { get; set; } = "Ctrl+D9"; // Ctrl+9
    public string Slot2PasteKey { get; set; } = "Ctrl+D0"; // Ctrl+0

    // Slot 3
    public string Slot3CopyKey { get; set; } = "Ctrl+OemOpenBrackets"; // Ctrl+[
    public string Slot3PasteKey { get; set; } = "Ctrl+OemCloseBrackets"; // Ctrl+]

    /// <summary>Milliseconds to wait after simulating Ctrl+C/V.</summary>
    public int SwapDelayMs { get; set; } = 250;

    /// <summary>Whether to show balloon tip notifications on copy/paste.</summary>
    public bool ShowNotifications { get; set; } = false;
}
