using System.Text.Json;
using System.Text.Json.Serialization;
using Zagrody.Core.Models;

namespace Zagrody.Core.Storage;

/// <summary>
/// Loads and saves <see cref="AppConfig"/> as JSON. Writes go to a temp file first and keep the
/// previous version as a .bak, so a crash mid-write never loses the user's layout.
/// </summary>
public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public ConfigStore(string path)
    {
        Path = path;
    }

    public string Path { get; }
    public string BackupPath => Path + ".bak";

    /// <summary>Set when the last <see cref="Load"/> had to fall back; meant to be shown to the user.</summary>
    public string? LoadWarning { get; private set; }

    public AppConfig Load()
    {
        LoadWarning = null;
        if (!File.Exists(Path))
        {
            return TryRead(BackupPath) ?? CreateNew();
        }

        if (TryRead(Path) is { } config)
        {
            return config;
        }

        var quarantine = $"{Path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
        TryMove(Path, quarantine);

        if (TryRead(BackupPath) is { } backup)
        {
            LoadWarning = $"Plik konfiguracji był uszkodzony, przywrócono kopię zapasową. Uszkodzony plik: {quarantine}";
            return backup;
        }

        LoadWarning = $"Plik konfiguracji był uszkodzony i nie było kopii zapasowej. Uszkodzony plik: {quarantine}";
        return CreateNew();
    }

    public void Save(AppConfig config)
    {
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = Path + ".tmp";
        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, config, JsonOptions);
            stream.Flush(flushToDisk: true);
        }

        if (File.Exists(Path))
        {
            File.Replace(tempPath, Path, BackupPath, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tempPath, Path);
        }
    }

    public static string Serialize(AppConfig config) => JsonSerializer.Serialize(config, JsonOptions);

    public static AppConfig Deserialize(string json)
    {
        var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions)
            ?? throw new JsonException("Config is empty.");
        config.Normalize();
        return config;
    }

    private static AppConfig? TryRead(string path)
    {
        try
        {
            return File.Exists(path) ? Deserialize(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    private static void TryMove(string from, string to)
    {
        try
        {
            File.Move(from, to, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Leaving the broken file in place is acceptable; the next save overwrites it.
        }
    }

    private static AppConfig CreateNew()
    {
        var config = new AppConfig();
        config.Normalize();
        return config;
    }
}
