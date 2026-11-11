using System.Security;
using GameShelf.Models;
using Microsoft.Win32;

namespace GameShelf.Library;

/// <summary>One place a launcher's games or pictures are read from, which the user may point elsewhere (spec §14).</summary>
public enum SourceEntry
{
    SteamFolder, SteamArtFolder, SteamCoverFiles, SteamBannerFile, SteamLogoFile, SteamDownload,
    EpicManifests, GogKey, UbisoftKey, RockstarKey,
}

/// <summary>What kind of value an entry holds, which decides how it's checked.</summary>
public enum SourceEntryKind { Folder, RegistryKey, FileName, FileNames, Address }

/// <summary>
/// Where every launcher's files, folders and registry keys and Steam's artwork are found (spec §14), so a user's
/// value replaces the default everywhere it's used. The values come from settings.json and count as untrusted:
/// each is checked again by <see cref="Validate"/> before use, and an invalid one falls back to the default.
/// What's read from the new place still goes through every source's own checks (ids, install folders, size caps).
/// </summary>
public static class SourceDefaults
{
    public static readonly SourceEntry[] All = Enum.GetValues<SourceEntry>();

    private static Dictionary<SourceEntry, string> _overrides = [];

    public static SourceEntryKind KindOf(SourceEntry entry) => entry switch
    {
        SourceEntry.SteamFolder or SourceEntry.SteamArtFolder or SourceEntry.EpicManifests => SourceEntryKind.Folder,
        SourceEntry.GogKey or SourceEntry.UbisoftKey or SourceEntry.RockstarKey => SourceEntryKind.RegistryKey,
        SourceEntry.SteamCoverFiles => SourceEntryKind.FileNames,
        SourceEntry.SteamBannerFile or SourceEntry.SteamLogoFile => SourceEntryKind.FileName,
        _ => SourceEntryKind.Address,
    };

    public static GameSource LauncherOf(SourceEntry entry) => entry switch
    {
        SourceEntry.EpicManifests => GameSource.Epic,
        SourceEntry.GogKey => GameSource.Gog,
        SourceEntry.UbisoftKey => GameSource.Ubisoft,
        SourceEntry.RockstarKey => GameSource.Rockstar,
        _ => GameSource.Steam,
    };

    /// <summary>The Steam entries that decide which pictures are found; changing one looks for pictures again.</summary>
    public static bool IsArtwork(SourceEntry entry) => entry is SourceEntry.SteamFolder or SourceEntry.SteamArtFolder
        or SourceEntry.SteamCoverFiles or SourceEntry.SteamBannerFile or SourceEntry.SteamLogoFile or SourceEntry.SteamDownload;

    // ---- Defaults -------------------------------------------------------------------------------------------

    public const string DefaultSteamRegistry = @"HKCU\Software\Valve\Steam › SteamPath";
    public const string DefaultArtSubfolder = @"appcache\librarycache";
    public static readonly string[] DefaultCoverFiles = ["library_600x900.jpg", "library_capsule.jpg"];
    public const string DefaultBannerFile = "library_hero.jpg";
    public const string DefaultLogoFile = "logo.png";
    public const string DefaultDownload = "https://shared.steamstatic.com/store_item_assets/steam/apps/{appid}/{file}";
    public const string DefaultGogKey = @"HKLM\SOFTWARE\WOW6432Node\GOG.com\Games";
    public const string DefaultUbisoftKey = @"HKLM\SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs";
    public const string DefaultRockstarKey = @"HKLM\SOFTWARE\WOW6432Node\Rockstar Games\Launcher";

    public static string DefaultEpicManifests => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");

    /// <summary>The default as the settings page shows it (the text box's placeholder).</summary>
    public static string DefaultText(SourceEntry entry) => entry switch
    {
        SourceEntry.SteamFolder => DefaultSteamFolder() ?? DefaultSteamRegistry,
        SourceEntry.SteamArtFolder => DefaultSteamFolder() is { } steam ? Path.Combine(steam, DefaultArtSubfolder) : @"{Steam}\" + DefaultArtSubfolder,
        SourceEntry.SteamCoverFiles => string.Join(", ", DefaultCoverFiles),
        SourceEntry.SteamBannerFile => DefaultBannerFile,
        SourceEntry.SteamLogoFile => DefaultLogoFile,
        SourceEntry.SteamDownload => DefaultDownload,
        SourceEntry.EpicManifests => DefaultEpicManifests,
        SourceEntry.GogKey => DefaultGogKey,
        SourceEntry.UbisoftKey => DefaultUbisoftKey,
        _ => DefaultRockstarKey,
    };

    /// <summary>Steam's folder as Steam records it, or null when Steam isn't installed.</summary>
    public static string? DefaultSteamFolder()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key?.GetValue("SteamPath") is not string path || path.Length == 0) return null;
            path = Path.GetFullPath(path.Replace('/', '\\'));
            return Directory.Exists(path) ? path : null;
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException or ArgumentException or PlatformNotSupportedException)
        {
            return null;
        }
    }

    // ---- The values in use ----------------------------------------------------------------------------------

    /// <summary>
    /// Takes the user's values (settings' <c>SourceOverrides</c>, entry name → value). Unknown names, empty and
    /// invalid values are left out, so those entries use their defaults. Call before a scan and after a change.
    /// </summary>
    public static void Use(IReadOnlyDictionary<string, string>? values)
    {
        var overrides = new Dictionary<SourceEntry, string>();
        if (values is not null)
            foreach (var (name, value) in values)
                if (Enum.TryParse<SourceEntry>(name, ignoreCase: false, out var entry) && Enum.IsDefined(entry) && !char.IsAsciiDigit(name[0]) &&
                    Validate(entry, value, out var normalized) == SourceValueError.None && normalized.Length > 0)
                    overrides[entry] = normalized;
        _overrides = overrides;
    }

    /// <summary>The user's value for the entry, or null when the default is used.</summary>
    public static string? Override(SourceEntry entry) => _overrides.GetValueOrDefault(entry);

    /// <summary>Steam's folder: the user's, else the one Steam records. Null when there's none.</summary>
    public static string? SteamFolder() =>
        Override(SourceEntry.SteamFolder) is { } folder ? (Directory.Exists(folder) ? folder : null) : DefaultSteamFolder();

    /// <summary>Steam's artwork cache: the user's folder, else librarycache in the Steam folder.</summary>
    public static string? SteamArtFolder() =>
        Override(SourceEntry.SteamArtFolder) ?? (SteamFolder() is { } steam ? Path.Combine(steam, DefaultArtSubfolder) : null);

    /// <summary>The portrait cover's file names, tried in order; the first is the one downloaded.</summary>
    public static string[] SteamCoverFiles() =>
        Override(SourceEntry.SteamCoverFiles) is { } names ? SplitNames(names) : DefaultCoverFiles;

    public static string SteamBannerFile() => Override(SourceEntry.SteamBannerFile) ?? DefaultBannerFile;
    public static string SteamLogoFile() => Override(SourceEntry.SteamLogoFile) ?? DefaultLogoFile;

    /// <summary>
    /// The address one picture is downloaded from. <paramref name="appId"/> must be a validated Steam appid and
    /// <paramref name="file"/> one of the checked file names.
    /// </summary>
    public static string SteamDownloadUrl(string appId, string file) =>
        (Override(SourceEntry.SteamDownload) ?? DefaultDownload).Replace("{appid}", appId).Replace("{file}", file);

    public static string EpicManifests() => Override(SourceEntry.EpicManifests) ?? DefaultEpicManifests;

    /// <summary>The launcher's registry key, opened read-only; null when it doesn't exist.</summary>
    public static RegistryKey? OpenKey(SourceEntry entry)
    {
        var path = Override(entry) ?? DefaultText(entry);
        if (!TryParseKey(path, out var hive, out var subKey)) return null;
        try
        {
            return (hive == RegistryHive.CurrentUser ? Registry.CurrentUser : Registry.LocalMachine).OpenSubKey(subKey, writable: false);
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException or ArgumentException or PlatformNotSupportedException)
        {
            return null;
        }
    }

    // ---- Checking a value -----------------------------------------------------------------------------------

    private const int MaxLength = 400;
    private const int MaxCoverFiles = 5;

    /// <summary>
    /// Checks a value for the entry and gives it back tidied up (full path, the hive's short name, names without
    /// spaces). Empty means "use the default" and is fine.
    /// </summary>
    public static SourceValueError Validate(SourceEntry entry, string? value, out string normalized)
    {
        normalized = "";
        value = value?.Trim() ?? "";
        if (value.Length == 0) return SourceValueError.None;
        if (value.Length > MaxLength || value.Any(char.IsControl)) return ErrorFor(KindOf(entry));
        switch (KindOf(entry))
        {
            case SourceEntryKind.Folder:
                if (!IsLocalFolder(value, out var folder)) return SourceValueError.Folder;
                normalized = folder;
                return SourceValueError.None;
            case SourceEntryKind.RegistryKey:
                if (!TryParseKey(value, out var hive, out var subKey)) return SourceValueError.RegistryKey;
                normalized = (hive == RegistryHive.CurrentUser ? @"HKCU\" : @"HKLM\") + subKey;
                return SourceValueError.None;
            case SourceEntryKind.FileName:
                if (!IsImageFileName(value)) return SourceValueError.FileName;
                normalized = value;
                return SourceValueError.None;
            case SourceEntryKind.FileNames:
                var names = SplitNames(value);
                if (names.Length is 0 or > MaxCoverFiles || !names.All(IsImageFileName)) return SourceValueError.FileNames;
                normalized = string.Join(", ", names);
                return SourceValueError.None;
            default:
                if (!IsDownloadAddress(value)) return SourceValueError.Address;
                normalized = value;
                return SourceValueError.None;
        }
    }

    private static SourceValueError ErrorFor(SourceEntryKind kind) => kind switch
    {
        SourceEntryKind.Folder => SourceValueError.Folder,
        SourceEntryKind.RegistryKey => SourceValueError.RegistryKey,
        SourceEntryKind.FileName => SourceValueError.FileName,
        SourceEntryKind.FileNames => SourceValueError.FileNames,
        _ => SourceValueError.Address,
    };

    private static string[] SplitNames(string value) =>
        value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    /// <summary>A full path on a local drive (not a network share) to a folder that exists.</summary>
    public static bool IsLocalFolder(string value, out string folder)
    {
        folder = "";
        try
        {
            if (!Path.IsPathFullyQualified(value) || value.StartsWith(@"\\", StringComparison.Ordinal) || value.StartsWith("//", StringComparison.Ordinal))
                return false;
            var full = Path.GetFullPath(value);
            if (full.StartsWith(@"\\", StringComparison.Ordinal)) return false;
            full = full.Length > 3 ? full.TrimEnd('\\', '/') : full;
            if (!Directory.Exists(full)) return false;
            folder = full;
            return true;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException or SecurityException)
        {
            return false;
        }
    }

    /// <summary>One file name (no folder, no drive) ending in .jpg, .jpeg or .png.</summary>
    public static bool IsImageFileName(string value) =>
        value.Length is > 4 and <= 100 &&
        value is not ("." or "..") &&
        value.IndexOfAny(['\\', '/', ':', ',']) < 0 && // no comma: the cover names are a comma-separated list
        value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
        !value.StartsWith('.') && !value.EndsWith(' ') &&
        (value.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || value.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
         value.EndsWith(".png", StringComparison.OrdinalIgnoreCase));

    /// <summary>A key under HKLM\SOFTWARE\ or HKCU\Software\ (long hive names allowed), without empty or ".." parts.</summary>
    public static bool TryParseKey(string value, out RegistryHive hive, out string subKey)
    {
        hive = RegistryHive.LocalMachine;
        subKey = "";
        var parts = value.Split('\\');
        if (parts.Length < 3 || parts.Any(p => p.Length == 0 || p.Trim() != p || p is "." or ".." || p.Contains('/'))) return false;
        hive = parts[0].ToUpperInvariant() switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE" => RegistryHive.LocalMachine,
            "HKCU" or "HKEY_CURRENT_USER" => RegistryHive.CurrentUser,
            _ => (RegistryHive)(-1),
        };
        if ((int)hive == -1 || !parts[1].Equals("SOFTWARE", StringComparison.OrdinalIgnoreCase)) return false;
        subKey = string.Join('\\', parts.Skip(1));
        return true;
    }

    /// <summary>
    /// An https:// address with {appid} in its path or query (and optionally {file}), no other placeholders, no
    /// user name or password, and a plain host.
    /// </summary>
    public static bool IsDownloadAddress(string value)
    {
        if (!value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || !value.Contains("{appid}", StringComparison.Ordinal)) return false;
        var rest = value.Replace("{appid}", "").Replace("{file}", "");
        if (rest.Contains('{') || rest.Contains('}') || rest.Any(char.IsWhiteSpace)) return false;
        var sample = value.Replace("{appid}", "1").Replace("{file}", "a.jpg");
        if (!Uri.TryCreate(sample, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length > 0 ||
            uri.HostNameType is not (UriHostNameType.Dns) || DownloadHost(value) is not { } host || host != uri.Host)
            return false;
        return true;
    }

    /// <summary>The address's host (from the part before any placeholder), or null when it can't be read.</summary>
    public static string? DownloadHost(string value)
    {
        int brace = value.IndexOf('{');
        var head = brace < 0 ? value : value[..brace];
        if (!head.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return null;
        var authority = head["https://".Length..];
        int slash = authority.IndexOfAny(['/', '?', '#']);
        if (slash < 0) return null; // a placeholder in the host
        return Uri.TryCreate("https://" + authority[..slash] + "/", UriKind.Absolute, out var uri) ? uri.Host : null;
    }

    /// <summary>The host the default address downloads from.</summary>
    public static string DefaultHost => DownloadHost(DefaultDownload)!;
}

/// <summary>Why a value was refused (None: fine).</summary>
public enum SourceValueError { None, Folder, RegistryKey, FileName, FileNames, Address }
