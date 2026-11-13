using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using Binder.Models;
using Microsoft.Win32;

namespace Binder.Library;

/// <summary>
/// Finds games installed with the EA app. Each one has an entry in Settings → Apps whose uninstaller is EA's
/// "Cleanup.exe uninstall_game"; its install folder holds __Installer\installerdata.xml with the content id
/// the EA app launches it by.
/// </summary>
public static partial class EaSource
{
    private static readonly string[] UninstallKeys =
    [
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
    ];

    /// <summary>The content id sits near the top of installerdata.xml (which can be several hundred KB).</summary>
    private const int InstallerDataHeadBytes = 64 * 1024;

    public static List<Game> Scan()
    {
        var games = new List<Game>();
        var seen = new HashSet<string>();
        foreach (var path in UninstallKeys)
        {
            try
            {
                using var root = Registry.LocalMachine.OpenSubKey(path);
                if (root is null) continue;
                foreach (var keyName in root.GetSubKeyNames())
                {
                    using var key = root.OpenSubKey(keyName);
                    if (key?.GetValue("UninstallString") is not string uninstall || !IsEaGame(uninstall)) continue;
                    var location = key.GetValue("InstallLocation") as string;
                    var game = Read(keyName,
                        key.GetValue("DisplayName") as string,
                        location,
                        key.GetValue("DisplayIcon") as string,
                        key.GetValue("EstimatedSize") is int kb ? kb : 0,
                        ReadContentId(location));
                    if (game is not null && seen.Add(game.Id)) games.Add(game);
                }
            }
            catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException) { }
        }
        return games;
    }

    /// <summary>EA's per-game uninstaller: "…\EAInstaller\{game}\Cleanup.exe" uninstall_game.</summary>
    public static bool IsEaGame(string uninstallString) =>
        uninstallString.Contains("EAInstaller", StringComparison.OrdinalIgnoreCase) &&
        uninstallString.Contains("Cleanup.exe", StringComparison.OrdinalIgnoreCase) &&
        uninstallString.Contains("uninstall_game", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Builds a game from one Settings → Apps entry. With a content id it starts through the EA app (which
    /// handles sign-in and updates), otherwise by its exe, which must be inside the install folder.
    /// </summary>
    public static Game? Read(string keyName, string? name, string? location, string? displayIcon, int sizeKb, string? contentId)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var folder = InstallFolder.Validate(location);
        if (folder is null) return null;

        string? exe = null;
        if (displayIcon is { Length: > 0 })
        {
            var path = displayIcon.Trim();
            int comma = path.LastIndexOf(',');
            if (comma > 0 && !path.EndsWith('"') && path[(comma + 1)..].Trim().All(c => char.IsAsciiDigit(c) || c == '-'))
                path = path[..comma]; // "game.exe,0"
            path = path.Trim().Trim('"');
            try
            {
                if (Path.IsPathFullyQualified(path))
                {
                    var full = Path.GetFullPath(path);
                    if (InstallFolder.IsSameOrInside(full, folder) && full.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(full))
                        exe = full;
                }
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { }
        }

        bool hasContentId = contentId is not null && IsContentId(contentId);
        if (!hasContentId && exe is null) return null; // nothing to start it with

        // The content id is stable across reinstalls; the registry key name is the fallback.
        var id = hasContentId ? contentId! : keyName.Trim('{', '}');
        if (!IsContentId(id)) return null;

        return new Game
        {
            Id = "ea:" + id,
            Source = GameSource.Ea,
            StoreId = hasContentId ? contentId : null,
            Name = CleanName(name),
            InstallDir = folder,
            LaunchUri = hasContentId ? $"origin2://game/launch?offerIds={contentId}&autoDownload=1" : null,
            ExePath = exe,
            SizeOnDisk = Math.Max(0, sizeKb) * 1024L,
        };
    }

    /// <summary>The first &lt;contentID&gt; in the game's __Installer\installerdata.xml, or null.</summary>
    public static string? ReadContentId(string? installLocation)
    {
        if (string.IsNullOrWhiteSpace(installLocation) || !Path.IsPathFullyQualified(installLocation)) return null;
        try
        {
            var file = Path.Combine(installLocation, "__Installer", "installerdata.xml");
            if (!File.Exists(file)) return null;
            using var stream = File.OpenRead(file);
            var buffer = new byte[(int)Math.Min(InstallerDataHeadBytes, stream.Length)];
            stream.ReadExactly(buffer);
            var match = ContentIdPattern().Match(Encoding.UTF8.GetString(buffer));
            return match.Success && IsContentId(match.Groups[1].Value) ? match.Groups[1].Value : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Content ids are digits, or dotted ids like "Origin.OFR.50.0001234": safe inside a link.</summary>
    public static bool IsContentId(string value) =>
        value.Length is > 0 and <= 64 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');

    /// <summary>"Battlefield™ 6" → "Battlefield 6".</summary>
    public static string CleanName(string name) =>
        name.Replace("™", "").Replace("®", "").Replace("©", "").Trim();

    [GeneratedRegex(@"<contentID>\s*([^<\s]+)\s*</contentID>", RegexOptions.CultureInvariant)]
    private static partial Regex ContentIdPattern();
}
