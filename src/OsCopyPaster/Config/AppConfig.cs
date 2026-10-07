namespace OsCopyPaster.Config;

/// <summary>
/// POCO model for config.json. All properties have sensible defaults.
/// </summary>
public class AppConfig
{
    /// <summary>Key combo string for the secondary copy trigger (e.g., "Ctrl+OemSemicolon").</summary>
    public string SecondaryCopyKey { get; set; } = "Ctrl+OemSemicolon";

    /// <summary>Key combo string for the secondary paste trigger (e.g., "Ctrl+OemQuotes").</summary>
    public string SecondaryPasteKey { get; set; } = "Ctrl+OemQuotes";

    /// <summary>Milliseconds to wait after simulating Ctrl+C/V for the target app to process.</summary>
    public int SwapDelayMs { get; set; } = 150;

    /// <summary>Milliseconds to wait for a digit press after the initial chord key.</summary>
    public int ChordTimeoutMs { get; set; } = 1000;

    /// <summary>Whether to show balloon tip notifications on copy/paste.</summary>
    public bool ShowNotifications { get; set; } = true;
}
