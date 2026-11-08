using System.Security;
using GameShelf.Library;
using GameShelf.Models;
using Microsoft.UI.Dispatching;
using Microsoft.Win32;

namespace GameShelf.Services;

/// <summary>
/// The launchers' own icons for the cover badges (spec §3), read at runtime from the installed launcher's exe,
/// so no trademarked artwork ships with the app. Read once at 32 px and cached as PNG.
/// </summary>
public static class LauncherIcons
{
    private const int Size = 32;
    private static readonly object Sync = new();
    private static readonly Dictionary<GameSource, IconPixels?> Loaded = [];
    private static readonly Dictionary<GameSource, List<Action<IconPixels?>>> Waiting = [];

    /// <summary>Manually added games and imported shortcuts get no badge.</summary>
    public static bool HasBadge(GameSource source) => source is not (GameSource.Manual or GameSource.Shortcut);

    /// <summary>
    /// The icon if it's loaded. Otherwise starts loading it and calls <paramref name="whenReady"/> on the UI
    /// thread once done (with null if the launcher's icon can't be read; show the glyph then).
    /// </summary>
    public static IconPixels? Get(GameSource source, Action<IconPixels?> whenReady)
    {
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        lock (Sync)
        {
            if (Loaded.TryGetValue(source, out var icon)) return icon;
            if (Waiting.TryGetValue(source, out var list))
            {
                list.Add(whenReady);
                return null;
            }
            Waiting[source] = [whenReady];
        }

        _ = LoadAsync(source).ContinueWith(task =>
        {
            var image = task.IsCompletedSuccessfully ? task.Result : null;
            List<Action<IconPixels?>> callbacks;
            lock (Sync)
            {
                Loaded[source] = image;
                callbacks = Waiting[source];
                Waiting.Remove(source);
            }
            dispatcher.TryEnqueue(() => { foreach (var callback in callbacks) callback(image); });
        }, TaskScheduler.Default);
        return null;
    }

    private static async Task<IconPixels?> LoadAsync(GameSource source)
    {
        var cached = Path.Combine(Storage.CacheDir, "covers", $"launcher-{source.ToString().ToLowerInvariant()}.png");
        if (await ShellImage.LoadPngAsync(cached).ConfigureAwait(false) is { } fromCache) return fromCache;

        if (await Task.Run(() => LauncherExe(source)).ConfigureAwait(false) is not { } exe) return null;
        var icon = await ShellImage.GetIconAsync(exe, Size).ConfigureAwait(false);
        if (icon is not null) await ShellImage.SavePngAsync(icon, cached).ConfigureAwait(false);
        return icon;
    }


    /// <summary>The installed launcher's exe: from its store path, or from the program registered for its links.</summary>
    internal static string? LauncherExe(GameSource source)
    {
        string? exe = source switch
        {
            GameSource.Steam => SteamSource.FindSteamPath() is { } steam ? Path.Combine(steam, "steam.exe") : ProtocolHandler("steam"),
            GameSource.Epic => ProtocolHandler("com.epicgames.launcher"),
            GameSource.Gog => ProtocolHandler("goggalaxy"),
            GameSource.Ea => ProtocolHandler("origin2"),
            GameSource.Ubisoft => ProtocolHandler("uplay"),
            GameSource.Rockstar => RockstarSource.LauncherExe(),
            GameSource.Hytale => HytaleSource.LauncherExe(),
            // A packaged app without a plain exe path: the shell item Start shows it as has its icon.
            GameSource.Xbox => XboxAppItem,
            _ => null,
        };
        return exe == XboxAppItem || exe is not null && File.Exists(exe) ? exe : null;
    }

    private const string XboxAppItem = @"shell:AppsFolder\Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App";

    /// <summary>The exe in HKCR\{scheme}\shell\open\command, e.g. "C:\…\EpicGamesLauncher.exe" %1.</summary>
    internal static string? ProtocolHandler(string scheme)
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey($@"{scheme}\shell\open\command");
            if (key?.GetValue(null) is not string command) return null;
            command = command.Trim();
            if (command.StartsWith('"'))
            {
                int end = command.IndexOf('"', 1);
                return end > 1 ? command[1..end] : null;
            }
            int exe = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            return exe > 0 ? command[..(exe + 4)] : null;
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
