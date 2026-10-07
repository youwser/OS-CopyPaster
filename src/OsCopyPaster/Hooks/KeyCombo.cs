using OsCopyPaster.Interop;

namespace OsCopyPaster.Hooks;

/// <summary>
/// Represents a modifier+key combination (e.g., Ctrl+OemSemicolon).
/// Supports parsing from config strings and matching against hook key events.
/// </summary>
public record struct KeyCombo(Keys Modifiers, Keys Key)
{
    /// <summary>
    /// Checks whether the given virtual key code, combined with the current
    /// modifier key state (via GetAsyncKeyState), matches this combo.
    /// </summary>
    public readonly bool Matches(uint vkCode)
    {
        if ((Keys)vkCode != Key)
            return false;

        bool ctrlRequired = (Modifiers & Keys.Control) != 0;
        bool shiftRequired = (Modifiers & Keys.Shift) != 0;
        bool altRequired = (Modifiers & Keys.Alt) != 0;

        bool ctrlDown = IsKeyDown(Keys.ControlKey);
        bool shiftDown = IsKeyDown(Keys.ShiftKey);
        bool altDown = IsKeyDown(Keys.Menu);

        return ctrlRequired == ctrlDown
            && shiftRequired == shiftDown
            && altRequired == altDown;
    }

    /// <summary>
    /// Parses a config string like "Ctrl+OemSemicolon" into a KeyCombo.
    /// Supported modifier names: Ctrl, Shift, Alt.
    /// The final token is parsed as a <see cref="Keys"/> enum value.
    /// </summary>
    public static KeyCombo Parse(string combo)
    {
        var parts = combo.Split('+');
        Keys modifiers = Keys.None;
        Keys key = Keys.None;

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            switch (trimmed.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= Keys.Control;
                    break;
                case "shift":
                    modifiers |= Keys.Shift;
                    break;
                case "alt":
                    modifiers |= Keys.Alt;
                    break;
                default:
                    if (Enum.TryParse<Keys>(trimmed, ignoreCase: true, out var parsed))
                        key = parsed;
                    break;
            }
        }

        return new KeyCombo(modifiers, key);
    }

    /// <summary>
    /// Converts the combo back to a human-readable string (e.g., "Ctrl+OemSemicolon").
    /// </summary>
    public override readonly string ToString()
    {
        var parts = new List<string>();
        if ((Modifiers & Keys.Control) != 0) parts.Add("Ctrl");
        if ((Modifiers & Keys.Shift) != 0) parts.Add("Shift");
        if ((Modifiers & Keys.Alt) != 0) parts.Add("Alt");
        parts.Add(Key.ToString());
        return string.Join("+", parts);
    }

    /// <summary>
    /// Returns a user-friendly display string (e.g., "Ctrl + ;" instead of "Ctrl+OemSemicolon").
    /// </summary>
    public readonly string ToDisplayString()
    {
        var parts = new List<string>();
        if ((Modifiers & Keys.Control) != 0) parts.Add("Ctrl");
        if ((Modifiers & Keys.Shift) != 0) parts.Add("Shift");
        if ((Modifiers & Keys.Alt) != 0) parts.Add("Alt");
        parts.Add(KeyToDisplayName(Key));
        return string.Join(" + ", parts);
    }

    private static string KeyToDisplayName(Keys key) => key switch
    {
        Keys.OemSemicolon => ";",
        Keys.OemQuotes => "'",
        Keys.OemPeriod => ".",
        Keys.Oemcomma => ",",
        Keys.OemMinus => "-",
        Keys.Oemplus => "=",
        Keys.OemOpenBrackets => "[",
        Keys.OemCloseBrackets => "]",
        Keys.OemBackslash => "\\",
        Keys.OemPipe => "|",
        Keys.OemQuestion => "/",
        Keys.Oemtilde => "`",
        _ => key.ToString()
    };

    private static bool IsKeyDown(Keys key)
    {
        return (NativeMethods.GetAsyncKeyState((int)key) & 0x8000) != 0;
    }
}
