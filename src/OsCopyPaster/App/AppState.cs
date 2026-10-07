namespace OsCopyPaster.App;

/// <summary>
/// Simple observable toggle state for enabling/disabling the hook.
/// When disabled, the hook remains installed but ignores all hotkeys.
/// </summary>
public class AppState
{
    private bool _isEnabled = true;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            OnToggled?.Invoke(_isEnabled);
        }
    }

    /// <summary>Fires when the enabled state changes. Parameter is the new state.</summary>
    public event Action<bool>? OnToggled;
}
