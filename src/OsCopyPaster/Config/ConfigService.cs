using System.Text.Json;

namespace OsCopyPaster.Config;

/// <summary>
/// Loads and saves <see cref="AppConfig"/> to/from config.json.
/// Creates a default config file on first run.
/// Fires <see cref="ConfigChanged"/> after every save so consumers can reload live.
/// </summary>
public class ConfigService
{
    private readonly string _configPath;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>Fired after a successful save. Consumers should reload their state.</summary>
    public event Action<AppConfig>? ConfigChanged;

    public ConfigService(string? configPath = null)
    {
        _configPath = configPath
            ?? Path.Combine(AppContext.BaseDirectory, "config.json");
    }

    /// <summary>
    /// Loads config from disk. Creates a default config file if it doesn't exist.
    /// Returns defaults on any deserialization error.
    /// </summary>
    public AppConfig Load()
    {
        if (!File.Exists(_configPath))
        {
            var defaults = new AppConfig();
            Save(defaults, raiseEvent: false);
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(_configPath);
            return JsonSerializer.Deserialize<AppConfig>(json, s_jsonOptions)
                ?? new AppConfig();
        }
        catch
        {
            return new AppConfig();
        }
    }

    /// <summary>
    /// Serializes and writes the config to disk, then fires <see cref="ConfigChanged"/>.
    /// </summary>
    public void Save(AppConfig config)
    {
        Save(config, raiseEvent: true);
    }

    private void Save(AppConfig config, bool raiseEvent)
    {
        try
        {
            var json = JsonSerializer.Serialize(config, s_jsonOptions);
            File.WriteAllText(_configPath, json);
        }
        catch
        {
            // Best-effort save — if the directory is read-only, silently fail.
        }

        if (raiseEvent)
            ConfigChanged?.Invoke(config);
    }
}
