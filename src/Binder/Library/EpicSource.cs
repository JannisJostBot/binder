using System.Text.Json;
using Binder.Models;

namespace Binder.Library;

/// <summary>
/// Finds installed Epic Games Store games from the launcher's manifests (*.item JSON files). Manifests sit in
/// ProgramData, which every local user can write to, so each one is validated before it becomes a game.
/// </summary>
public static class EpicSource
{
    /// <summary>Manifests are a few KB.</summary>
    public const long MaxManifestBytes = 1024 * 1024;

    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        MaxDepth = 32,
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public static string ManifestFolder => SourceDefaults.EpicManifests();

    public static List<Game> Scan() => Scan(ManifestFolder);

    public static List<Game> Scan(string folder)
    {
        var games = new List<Game>();
        try
        {
            if (!Directory.Exists(folder)) return games;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(folder, "*.item"))
            {
                var game = ReadFile(file);
                if (game is not null && seen.Add(game.Id)) games.Add(game);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return games;
    }

    public static Game? ReadFile(string path)
    {
        try
        {
            if (new FileInfo(path).Length > MaxManifestBytes) return null;
            ReadOnlyMemory<byte> bytes = File.ReadAllBytes(path);
            // JsonDocument rejects a UTF-8 byte order mark; files saved by some editors and tools have one.
            if (bytes.Span.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) bytes = bytes[3..];
            using var doc = JsonDocument.Parse(bytes, JsonOptions);
            return Read(doc.RootElement);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static Game? Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;

        var name = Str(root, "DisplayName");
        var location = Str(root, "InstallLocation");
        var appName = Str(root, "AppName");
        var ns = Str(root, "CatalogNamespace");
        var itemId = Str(root, "CatalogItemId");
        if (string.IsNullOrWhiteSpace(name) || appName is null || ns is null || itemId is null) return null;

        // These go into a launcher URI: only plain id characters, so nothing can be injected into it.
        if (!IsEpicId(appName) || !IsEpicId(ns) || !IsEpicId(itemId)) return null;

        if (root.TryGetProperty("bIsIncompleteInstall", out var incomplete) && incomplete.ValueKind == JsonValueKind.True)
            return null;
        if (!IsGame(root)) return null;
        // Add-ons (HD texture, sound and voice packs) have their own manifest that names the game they belong to.
        if (Str(root, "MainGameAppName") is { Length: > 0 } main && !string.Equals(main, appName, StringComparison.OrdinalIgnoreCase))
            return null;

        var folder = InstallFolder.Validate(location);
        if (folder is null) return null;

        // The game's exe, for its icon. Must stay inside the install folder.
        string? icon = null;
        if (Str(root, "LaunchExecutable") is { Length: > 0 } exe)
        {
            try
            {
                var full = Path.GetFullPath(Path.Combine(folder, exe));
                if (InstallFolder.IsSameOrInside(full, folder) && full.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    icon = full;
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { }
        }

        long size = root.TryGetProperty("InstallSize", out var s) && s.ValueKind == JsonValueKind.Number &&
                    s.TryGetInt64(out var bytes) ? bytes : 0;

        return new Game
        {
            Id = "epic:" + appName,
            Source = GameSource.Epic,
            StoreId = appName,
            Name = name.Trim(),
            InstallDir = folder,
            LaunchUri = $"com.epicgames.launcher://apps/{ns}%3A{itemId}%3A{appName}?action=launch&silent=true",
            SizeOnDisk = size,
            IconPath = icon,
        };
    }

    /// <summary>Epic ids are short runs of letters, digits, '-', '_' and '.'.</summary>
    public static bool IsEpicId(string value) =>
        value.Length is > 0 and <= 128 &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');

    /// <summary>The launcher also installs engines and plugins; when categories are listed, require "games".</summary>
    private static bool IsGame(JsonElement root)
    {
        if (!root.TryGetProperty("AppCategories", out var categories) || categories.ValueKind != JsonValueKind.Array)
            return true;
        bool any = false;
        foreach (var category in categories.EnumerateArray())
        {
            if (category.ValueKind != JsonValueKind.String) continue;
            any = true;
            if (string.Equals(category.GetString(), "games", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return !any;
    }

    private static string? Str(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
