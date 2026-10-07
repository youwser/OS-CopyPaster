namespace OsCopyPaster.Hooks;

/// <summary>The type of secondary clipboard operation.</summary>
public enum HotkeyType
{
    SecondaryCopy,
    SecondaryPaste
}

/// <summary>
/// Represents a resolved hotkey action: which operation and which slot (0-9).
/// </summary>
public record HotkeyAction(HotkeyType Type, int Slot);
