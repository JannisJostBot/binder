using System.Security;
using GameShelf.Models;
using Microsoft.Win32;

namespace GameShelf.Library;

/// <summary>
/// Finds Hytale, installed by the Hytale Launcher (Hypixel Studios). The launcher's installer records two keys:
/// HKLM\SOFTWARE\Hypixel Studios\Hytale Launcher (InstallDir, with hytale-launcher.exe) and
/// HKLM\SOFTWARE\Hypixel Studios\Hytale (GameInstallPath, usually %APPDATA%\Hytale). The game lives under
/// {GameInstallPath}\install\{patchline}\package\game\latest\Client\HytaleClient.exe, next to its own Java runtime.
/// The client needs the launcher's sign-in, so Play opens the launcher and the watcher follows whatever runs from
/// the install folder (the client, and the local server of a singleplayer world).
/// </summary>
public static class HytaleSource
{
    private const string LauncherKey = @"SOFTWARE\Hypixel Studios\Hytale Launcher";
    private const string GameKey = @"SOFTWARE\Hypixel Studios\Hytale";
    private const string ClientPath = @"package\game\latest\Client\HytaleClient.exe";
    private const int MaxPatchlines = 8;
    private const int MaxFilesCounted = 50_000;

    public static List<Game> Scan()
    {
        if (LauncherExe() is null) return [];
        if (ReadPath(GameKey, "GameInstallPath") is not { } gameFolder) return [];
        return Read(gameFolder) is { } game ? [game] : [];
    }

    /// <summary>Builds the game from its folder (GameInstallPath); null when it isn't installed there or isn't safe.</summary>
    public static Game? Read(string gameFolder)
    {
        if (InstallFolder.Validate(gameFolder) is not { } folder) return null;
        var install = InstallFolder.Validate(Path.Combine(folder, "install"));
        if (install is null) return null;
        if (Client(install) is not { } client) return null; // the launcher is there, the game not yet downloaded

        return new Game
        {
            Id = "hytale:hytale",
            Source = GameSource.Hytale,
            StoreId = "hytale",
            Name = "Hytale",
            InstallDir = install,
            SizeOnDisk = SizeOf(install),
            IconPath = client,
        };
    }

    /// <summary>The client of the patchline in use ("release" first, then any other), or null.</summary>
    internal static string? Client(string install)
    {
        var release = Path.Combine(install, "release", ClientPath);
        if (File.Exists(release)) return release;
        try
        {
            foreach (var patchline in Directory.EnumerateDirectories(install).Take(MaxPatchlines))
            {
                var exe = Path.Combine(patchline, ClientPath);
                if (File.Exists(exe)) return exe;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException) { }
        return null;
    }

    /// <summary>The install folder's size (a few thousand files, mostly one big asset archive); bounded.</summary>
    private static long SizeOf(string folder)
    {
        long total = 0;
        int count = 0;
        try
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", options))
            {
                total += file.Length;
                if (++count >= MaxFilesCounted) break;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException) { }
        return total;
    }

    /// <summary>The installed Hytale Launcher (hytale-launcher.exe), or null.</summary>
    public static string? LauncherExe()
    {
        if (InstallFolder.Validate(ReadPath(LauncherKey, "InstallDir")) is not { } folder) return null;
        var exe = Path.Combine(folder, "hytale-launcher.exe");
        return File.Exists(exe) ? exe : null;
    }

    private static string? ReadPath(string keyPath, string value)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath);
            return key?.GetValue(value) as string is { Length: > 0 and < 1024 } path ? path : null;
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
