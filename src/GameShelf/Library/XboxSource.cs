using System.Net;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using GameShelf.Models;
using Microsoft.Win32;

namespace GameShelf.Library;

/// <summary>
/// Finds games installed with the Xbox app (Game Pass and Microsoft Store PC games, milestone 12). The Xbox app
/// installs them into an "XboxGames" folder on each drive, which a ".GamingRoot" file in the drive's root names.
/// Each game folder holds Content\appxmanifest.xml (package name and app id) and MicrosoftGame.config (display
/// name and exe). Only packages Windows has registered for this user count: their family name comes from the
/// package records in HKCU, and the game launches as shell:AppsFolder\{family}!{app}, like Start does.
/// </summary>
public static partial class XboxSource
{
    private const string PackagesKey =
        @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

    private const int MaxGamingRootBytes = 4096;
    private const int MaxManifestBytes = 256 * 1024;
    private const int MaxGames = 500;

    public static List<Game> Scan()
    {
        var games = new List<Game>();
        var families = RegisteredFamilies();
        if (families.Count == 0) return games;
        var seen = new HashSet<string>();
        foreach (var root in GameRoots())
        {
            try
            {
                foreach (var folder in Directory.EnumerateDirectories(root))
                {
                    var content = Path.Combine(folder, "Content");
                    var manifest = ReadHead(Path.Combine(content, "appxmanifest.xml"));
                    if (manifest is null) continue;
                    var game = Read(folder, manifest, ReadHead(Path.Combine(content, "MicrosoftGame.config")), families);
                    if (game is not null && seen.Add(game.Id)) games.Add(game);
                    if (games.Count >= MaxGames) return games;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException) { }
        }
        return games;
    }

    /// <summary>The XboxGames folders: for each fixed drive with a .GamingRoot file, the folders it names.</summary>
    private static List<string> GameRoots()
    {
        var roots = new List<string>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                var file = Path.Combine(drive.RootDirectory.FullName, ".GamingRoot");
                var info = new FileInfo(file);
                if (!info.Exists || info.Length > MaxGamingRootBytes) continue;
                foreach (var name in ParseGamingRoot(File.ReadAllBytes(file)))
                {
                    var folder = Path.Combine(drive.RootDirectory.FullName, name);
                    if (Directory.Exists(folder)) roots.Add(folder);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException) { }
        }
        return roots;
    }

    /// <summary>
    /// A .GamingRoot file: "RGBX", a 32-bit count, then that many null-terminated UTF-16 folder names relative to
    /// the drive root (e.g. "XboxGames"). Only plain relative names are returned, so it can't point elsewhere.
    /// </summary>
    public static List<string> ParseGamingRoot(byte[] data)
    {
        var names = new List<string>();
        if (data.Length < 8 || data[0] != 'R' || data[1] != 'G' || data[2] != 'B' || data[3] != 'X') return names;
        uint count = BitConverter.ToUInt32(data, 4);
        int pos = 8;
        for (uint i = 0; i < Math.Min(count, 16u) && pos + 1 < data.Length; i++)
        {
            int end = pos;
            while (end + 1 < data.Length && (data[end] != 0 || data[end + 1] != 0)) end += 2;
            var name = Encoding.Unicode.GetString(data, pos, end - pos);
            pos = end + 2;
            if (IsPlainRelative(name)) names.Add(name);
        }
        return names;
    }

    private static bool IsPlainRelative(string name) =>
        name.Length is > 0 and <= 200 && !Path.IsPathRooted(name) && name.IndexOfAny(Path.GetInvalidPathChars()) < 0 &&
        !name.Contains(':') && name.Split('\\', '/').All(part => part.Length > 0 && part != "." && part != "..");

    /// <summary>
    /// Package name → publisher id for every package Windows has registered for this user, from the package
    /// records' key names ("Name_Version_Arch_Resource_PublisherId"). Package names can't contain "_".
    /// </summary>
    private static Dictionary<string, string> RegisteredFamilies()
    {
        var families = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PackagesKey);
            if (key is null) return families;
            foreach (var fullName in key.GetSubKeyNames())
                if (FamilyOf(fullName) is var (name, publisherId)) families.TryAdd(name, publisherId);
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException) { }
        return families;
    }

    /// <summary>"Microsoft.624F8B84B80_3.688.109.0_x64__8wekyb3d8bbwe" → ("Microsoft.624F8B84B80", "8wekyb3d8bbwe").</summary>
    public static (string Name, string PublisherId)? FamilyOf(string packageFullName)
    {
        var parts = packageFullName.Split('_');
        if (parts.Length != 5) return null;
        return IsPackageName(parts[0]) && IsPublisherId(parts[4]) ? (parts[0], parts[4]) : null;
    }

    /// <summary>Builds a game from one XboxGames folder; null for anything incomplete, unregistered or unsafe.</summary>
    public static Game? Read(string folder, string manifest, string? config, IReadOnlyDictionary<string, string> families)
    {
        if (TagAttribute(manifest, "Identity", "Name") is not { } packageName || !IsPackageName(packageName)) return null;
        if (TagAttribute(manifest, "Application", "Id") is not { } appId || !IsAppId(appId)) return null;
        if (!families.TryGetValue(packageName, out var publisherId)) return null; // not installed for this user
        var dir = InstallFolder.Validate(folder);
        if (dir is null) return null;

        // The manifest's name can be a resource reference ("ms-resource:…"); MicrosoftGame.config usually has
        // the plain one. The folder is named after the game by the Xbox app, as a last resort.
        var name = Plain(DisplayNameElement(manifest)) ??
                   Plain(config is null ? null : TagAttribute(config, "ShellVisuals", "DefaultDisplayName")) ??
                   Path.GetFileName(dir);

        // The generated cover's picture: the game's tile logo (what Start shows for it), else its exe's icon.
        // Xbox game exes often carry no icon of their own.
        var icon = ContentFile(dir, config is null ? null : TagAttribute(config, "ShellVisuals", "Square150x150Logo"), ".png") ??
                   ContentFile(dir, config is null ? null : TagAttribute(config, "Executable", "Name"), ".exe");

        var family = packageName + "_" + publisherId;
        return new Game
        {
            Id = "xbox:" + packageName,
            Source = GameSource.Xbox,
            StoreId = family,
            Name = name,
            InstallDir = dir,
            LaunchUri = $@"shell:AppsFolder\{family}!{appId}",
            IconPath = icon,
        };
    }

    /// <summary>A plain file name from the config, as an existing file in the game's Content folder.</summary>
    private static string? ContentFile(string dir, string? fileName, string extension)
    {
        if (fileName is null || fileName.Length > 100 || !fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ||
            fileName.IndexOfAny(['\\', '/', ':']) >= 0) return null;
        var path = Path.Combine(dir, "Content", fileName);
        return File.Exists(path) ? path : null;
    }

    private static string? Plain(string? name) =>
        string.IsNullOrWhiteSpace(name) || name.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase) ? null : name.Trim();

    /// <summary>The first &lt;tag …&gt; element's attribute, entity-decoded (tags may carry a namespace prefix).</summary>
    private static string? TagAttribute(string xml, string tag, string attribute)
    {
        var element = Regex.Match(xml, $@"<(?:\w+:)?{tag}\s[^>]*>", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!element.Success) return null;
        var value = Regex.Match(element.Value, $@"\s{attribute}\s*=\s*""([^""]*)""", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return value.Success ? WebUtility.HtmlDecode(value.Groups[1].Value) : null;
    }

    private static string? DisplayNameElement(string manifest)
    {
        var m = DisplayNamePattern().Match(manifest);
        return m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value) : null;
    }

    /// <summary>The first part of a text file, or null if it's missing or unreadable.</summary>
    private static string? ReadHead(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[(int)Math.Min(stream.Length, MaxManifestBytes)];
            int read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            using var reader = new StreamReader(new MemoryStream(buffer, 0, read), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        {
            return null;
        }
    }

    /// <summary>Package names: 3–50 letters, digits, "." and "-" (the AppX rules).</summary>
    public static bool IsPackageName(string s) =>
        s.Length is >= 3 and <= 50 && s.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-');

    /// <summary>Publisher ids: 13 characters of Windows' base32 alphabet.</summary>
    public static bool IsPublisherId(string s) => s.Length == 13 && s.All(c => char.IsAsciiDigit(c) || char.IsAsciiLetterLower(c));

    /// <summary>App ids: a letter, then letters, digits and "." (at most 64).</summary>
    public static bool IsAppId(string s) =>
        s.Length is > 0 and <= 64 && char.IsAsciiLetter(s[0]) && s.All(c => char.IsAsciiLetterOrDigit(c) || c == '.');

    [GeneratedRegex(@"<(?:\w+:)?DisplayName>\s*([^<]*?)\s*</(?:\w+:)?DisplayName>", RegexOptions.CultureInvariant)]
    private static partial Regex DisplayNamePattern();
}
