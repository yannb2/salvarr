using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace Salvarr.Options;

/// <summary>
/// The settings changed on the Settings page: Radarr and Sonarr. They are kept in
/// <c>/config/settings.json</c> and win over the environment variables (Salvarr__Radarr__Url...),
/// which stay as the starting values until the page saves something. As in Ripparr.
/// </summary>
public sealed class LiveSettings(IOptions<SalvarrOptions> options, ILogger<LiveSettings> logger)
{
    public const string RadarrKey = "integrations.radarr";
    public const string SonarrKey = "integrations.sonarr";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly object _lock = new();
    private JsonObject? _values;

    /// <summary>The file the page saves to.</summary>
    public string FilePath => Path.Combine(options.Value.ConfigFolder, "settings.json");

    public ArrOptions Radarr => Read(RadarrKey, options.Value.Radarr);

    public ArrOptions Sonarr => Read(SonarrKey, options.Value.Sonarr);

    /// <summary>True when the page saved this setting; false when it still comes from the environment.</summary>
    public bool Saved(string key)
    {
        lock (_lock)
        {
            return Values().ContainsKey(key);
        }
    }

    /// <summary>Saves a value. Returns null, or why it could not be written (no writable /config).</summary>
    public string? Save<T>(string key, T value) => Write(values => values[key] = JsonSerializer.SerializeToNode(value, Json));

    /// <summary>Forgets what the page saved: the environment variables apply again.</summary>
    public string? Reset(string key) => Write(values => values.Remove(key));

    private string? Write(Action<JsonObject> change)
    {
        lock (_lock)
        {
            var values = (JsonObject)Values().DeepClone();
            change(values);
            try
            {
                Directory.CreateDirectory(options.Value.ConfigFolder);
                var temp = FilePath + ".tmp";
                File.WriteAllText(temp, values.ToJsonString(Json));
                File.Move(temp, FilePath, overwrite: true);
                _values = values;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(e, "Could not save {File}", FilePath);
                return $"Could not save {FilePath}: {e.Message} Mount a writable folder at {options.Value.ConfigFolder}.";
            }
        }

        return null;
    }

    private T Read<T>(string key, T fallback) where T : class
    {
        lock (_lock)
        {
            if (Values()[key] is not { } node)
            {
                return fallback;
            }

            try
            {
                return node.Deserialize<T>(Json) ?? fallback;
            }
            catch (JsonException)
            {
                return fallback;
            }
        }
    }

    /// <summary>The saved values, read from the file once.</summary>
    private JsonObject Values()
    {
        if (_values != null)
        {
            return _values;
        }

        try
        {
            _values = File.Exists(FilePath) ? JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject ?? [] : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(e, "Could not read {File}; using the environment variables", FilePath);
            _values = [];
        }

        return _values;
    }
}
