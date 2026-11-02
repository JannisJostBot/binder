using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using GameShelf.Models;

namespace GameShelf.Services;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(List<Game>))]
[JsonSerializable(typeof(List<Session>))]
[JsonSerializable(typeof(Settings))]
[JsonSerializable(typeof(List<ImportedShortcut>))]
internal sealed partial class StorageJsonContext : JsonSerializerContext;

/// <summary>JSON persistence in %APPDATA%\GameShelf. Source-generated, no reflection.</summary>
public static class Storage
{
    public static readonly string DataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GameShelf");

    public static readonly string CacheDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameShelf");

    private static string LibraryPath => Path.Combine(DataDir, "library.json");
    private static string SettingsPath => Path.Combine(DataDir, "settings.json");
    private static string SessionsPath => Path.Combine(DataDir, "sessions.json");
    private static string ImportedPath => Path.Combine(DataDir, "imported-shortcuts.json");

    public static List<Game> LoadLibrary() => Load(LibraryPath, StorageJsonContext.Default.ListGame) ?? [];
    public static void SaveLibrary(List<Game> games) => Save(LibraryPath, games, StorageJsonContext.Default.ListGame);

    public static Settings LoadSettings() => Load(SettingsPath, StorageJsonContext.Default.Settings) ?? new Settings();
    public static void SaveSettings(Settings s) => Save(SettingsPath, s, StorageJsonContext.Default.Settings);

    public static List<Session> LoadSessions() => Load(SessionsPath, StorageJsonContext.Default.ListSession) ?? [];

    public static List<ImportedShortcut> LoadImportedShortcuts() =>
        Load(ImportedPath, StorageJsonContext.Default.ListImportedShortcut) ?? [];
    public static void SaveImportedShortcuts(List<ImportedShortcut> list) =>
        Save(ImportedPath, list, StorageJsonContext.Default.ListImportedShortcut);

    private static T? Load<T>(string path, JsonTypeInfo<T> info) where T : class
    {
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, info);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // Missing or corrupt file: start fresh rather than crash.
            return null;
        }
    }

    /// <summary>Writes to a temp file and swaps it in, so a crash never leaves a half-written file.</summary>
    private static void Save<T>(string path, T value, JsonTypeInfo<T> info)
    {
        Directory.CreateDirectory(DataDir);
        var tmp = path + ".tmp";
        using (var stream = File.Create(tmp))
            JsonSerializer.Serialize(stream, value, info);
        File.Move(tmp, path, overwrite: true);
    }
}
