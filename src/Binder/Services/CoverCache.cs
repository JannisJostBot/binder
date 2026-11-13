using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using Binder.Library;
using Binder.Models;
using Binder.Views;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Binder.Services;

/// <summary>Which artwork to load: the 600×900 portrait cover, the wide hero banner, or the game's transparent logo (§13 a).</summary>
public enum ArtKind { Cover, Hero, Logo }

/// <summary>What a generated cover is made of: the game's icon (if any) and its background color.</summary>
public sealed record FallbackArtInfo(IconPixels? Icon, string Tint);

/// <summary>
/// Finds and decodes cover art. Sources, in order: the user's custom cover, our disk cache, Steam's local
/// library cache, then a one-time download from the Steam CDN into our cache. Files are found off the UI
/// thread; WinUI decodes them in the background at the tile's pixel size. A small in-memory LRU of decoded
/// images (UI thread only) avoids re-decoding while filtering and scrolling.
/// </summary>
public static class CoverCache
{
    private const int MemoryEntries = 48;

    /// <summary>Steam art is well under 1 MB; refuse anything far larger instead of filling the disk.</summary>
    private const long MaxDownloadBytes = 8 * 1024 * 1024;

    private static readonly string CoversDir = Path.Combine(Storage.CacheDir, "covers");
    private static readonly SemaphoreSlim DownloadGate = new(2);
    // Steam's artwork folder (SourceDefaults), looked up once; forgotten when an artwork entry changes.
    private static Lazy<string?> SteamLibraryCache = new(SourceDefaults.SteamArtFolder);
    // Created on the first download only (most starts download nothing), and only once even when two run at once.
    private static readonly Lazy<HttpClient> Http = new(() => new HttpClient { Timeout = TimeSpan.FromSeconds(20) });

    // Resolved file path per game id (null = no cover). Avoids re-probing the disk on every realization.
    private static readonly Dictionary<string, string?> Resolved = [];
    private static readonly Dictionary<string, LinkedListNode<(string Key, ImageSource Image)>> Memory = [];
    private static readonly LinkedList<(string Key, ImageSource Image)> Lru = new();
    private static readonly object Sync = new();

    // Bumped whenever cached lookups are dropped (a custom cover set, a game forgotten, artwork places changed): a
    // lookup that was already running then doesn't write its out-of-date answer back.
    private static int _generation;

    // Covers that couldn't be downloaded because Steam wasn't reachable (offline, Wi-Fi without internet). Shown as
    // generated covers for now and looked up again the next time the panel opens, never on a timer. After the first
    // such failure the rest wait too, so a connection without internet costs one timeout, not one per game.
    private static bool _offline;
    private static readonly HashSet<string> RetryKeys = [];

    /// <summary>
    /// The panel opened: covers that couldn't be downloaded last time (no connection) are tried again when shown.
    /// </summary>
    public static void PanelOpened()
    {
        lock (Sync)
        {
            _offline = false;
            foreach (var key in RetryKeys) Resolved.Remove(key);
            RetryKeys.Clear();
        }
    }

    /// <summary>Returns an already decoded cover without any I/O, so recycled tiles don't flash a placeholder.</summary>
    public static ImageSource? TryGetCached(Game game, int pixelWidth, ArtKind kind = ArtKind.Cover)
    {
        lock (Sync)
        {
            if (!Memory.TryGetValue(Key(game, pixelWidth, kind), out var node)) return null;
            Lru.Remove(node);
            Lru.AddFirst(node);
            return node.Value.Image;
        }
    }

    /// <summary>The cover at <paramref name="pixelWidth"/>, or null if there's none. Call on the UI thread.</summary>
    public static async Task<ImageSource?> LoadAsync(Game game, int pixelWidth, ArtKind kind = ArtKind.Cover)
    {
        var path = await ResolveAsync(game, kind); // back on the UI thread
        if (path is null) return null;
        try
        {
            var image = new BitmapImage { DecodePixelWidth = pixelWidth, DecodePixelType = DecodePixelType.Physical };
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var stream = file.AsRandomAccessStream())
                await image.SetSourceAsync(stream);
            Remember(Key(game, pixelWidth, kind), image);
            return image;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or COMException or ArgumentException)
        {
            return null; // missing or not an image we can decode: the generated cover takes over
        }
    }

    // One lookup (and at most one download) per game and art kind at a time: a tile and the continue card
    // asking for the same cover at once used to download it twice, and the loser's failure was remembered.
    private static readonly Dictionary<string, Task<string?>> Resolving = [];

    private static Task<string?> ResolveAsync(Game game, ArtKind kind)
    {
        var key = ResolvedKey(game, kind);
        lock (Sync)
        {
            if (Resolved.TryGetValue(key, out var known)) return Task.FromResult(known);
            if (Resolving.TryGetValue(key, out var running)) return running;
            int generation = _generation;
            var task = Task.Run(async () =>
            {
                var local = FindLocal(game, kind);
                var (path, retry) = local is not null ? (local, false) : await DownloadAsync(game, kind).ConfigureAwait(false);
                lock (Sync)
                {
                    if (generation == _generation)
                    {
                        Resolved[key] = path;
                        if (retry) RetryKeys.Add(key);
                    }
                    Resolving.Remove(key);
                }
                return path;
            });
            Resolving[key] = task;
            return task;
        }
    }


    /// <summary>
    /// Copies a user-picked image into our cache (so moving or deleting the original doesn't break the cover)
    /// and returns the copy's path. Replaces any previous custom cover copy for the game.
    /// </summary>
    public static string ImportCustomCover(Game game, string sourcePath)
    {
        var dir = Path.Combine(CoversDir, "custom");
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, SafeId(game) + Path.GetExtension(sourcePath).ToLowerInvariant());
        File.Copy(sourcePath, target, overwrite: true);
        DeleteOwnCopy(game.CustomCover, except: target);
        Invalidate(game);
        return target;
    }

    /// <summary>Goes back to the store cover. Deletes only our own copy, never a file of the user's.</summary>
    public static void ClearCustomCover(Game game)
    {
        DeleteOwnCopy(game.CustomCover, except: null);
        game.CustomCover = null;
        Invalidate(game);
    }

    private static void DeleteOwnCopy(string? path, string? except)
    {
        if (string.IsNullOrEmpty(path) || string.Equals(path, except, StringComparison.OrdinalIgnoreCase)) return;
        var customDir = Path.Combine(CoversDir, "custom") + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(path).StartsWith(customDir, StringComparison.OrdinalIgnoreCase)) return;
        try { File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    // Generated covers (spec §2): the game's icon, read once and cached as a 256 px PNG.
    private const int IconSize = 256;
    private static readonly Dictionary<string, IconPixels?> Icons = [];
    private static readonly Dictionary<string, ImageSource> IconImages = [];

    /// <summary>
    /// What the generated cover for a game without store art is made of: its icon on its own color, or a
    /// monogram tone. The icon's color is stored on the game (library.json), worked out once.
    /// </summary>
    public static async Task<FallbackArtInfo> GetFallbackAsync(Game game)
    {
        var icon = await LoadIconAsync(game).ConfigureAwait(true);
        if (icon is not null && game.CoverTint is null)
            game.CoverTint = await Task.Run(() => CoverColors.IconTint(icon.Bgra, icon.Width, icon.Height)).ConfigureAwait(true);
        var tint = icon is not null ? game.CoverTint : null;
        return new FallbackArtInfo(icon, tint ?? CoverColors.MonogramTone(game.Name));
    }

    /// <summary>
    /// The generated cover's parts without any I/O, when it's already known that the game has no art and its
    /// icon was already read: a tile shown again (after a rescan, or recycled) draws it at once instead of
    /// going back to the placeholder.
    /// </summary>
    public static FallbackArtInfo? TryGetFallback(Game game, ArtKind kind)
    {
        lock (Sync)
        {
            if (!Resolved.TryGetValue(ResolvedKey(game, kind), out var path) || path is not null) return null;
            if (!Icons.TryGetValue(game.Id, out var icon)) return null;
            if (icon is null) return new FallbackArtInfo(null, CoverColors.MonogramTone(game.Name));
            return game.CoverTint is { } tint ? new FallbackArtInfo(icon, tint) : null; // color not worked out yet
        }
    }

    /// <summary>The icon as a UI image, one per game, shared by all tiles showing it. UI thread only.</summary>
    public static ImageSource IconImage(Game game, IconPixels icon)
    {
        lock (Sync)
        {
            if (IconImages.TryGetValue(game.Id, out var known)) return known;
            var bitmap = Bitmaps.FromPixels(icon);
            IconImages[game.Id] = bitmap;
            return bitmap;
        }
    }

    // One Shell icon extraction per game at a time: a tile and the continue card can ask together.
    private static readonly Dictionary<string, Task<IconPixels?>> LoadingIcons = [];

    private static Task<IconPixels?> LoadIconAsync(Game game)
    {
        lock (Sync)
        {
            if (Icons.TryGetValue(game.Id, out var known)) return Task.FromResult(known);
            if (LoadingIcons.TryGetValue(game.Id, out var running)) return running;
            var task = Task.Run(() => ExtractIconAsync(game)); // never completes inside this lock
            LoadingIcons[game.Id] = task;
            return task;
        }
    }

    private static async Task<IconPixels?> ExtractIconAsync(Game game)
    {
        IconPixels? icon = null;
        if (game.IconSource is { } exe)
        {
            // "_icon2": icons cached before exes were read directly may be the Shell's generic stand-in.
            var cached = CachedFile(game, ArtKind.Cover, "_icon2.png");
            icon = await ShellImage.LoadPngAsync(cached).ConfigureAwait(false);
            if (icon is null)
            {
                DeleteQuietly(CachedFile(game, ArtKind.Cover, "_icon.png"));
                game.CoverTint = null; // its color may come from the old icon: work it out again
                // A .png or .jpg (an Xbox tile logo, Steam's cached icon) is the picture itself; else ask for its icon.
                icon = exe.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || exe.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                    ? await ShellImage.LoadPngAsync(exe).ConfigureAwait(false)
                    : await ShellImage.GetIconAsync(exe, IconSize).ConfigureAwait(false);
                if (icon is not null) await ShellImage.SavePngAsync(icon, cached).ConfigureAwait(false);
            }
        }
        lock (Sync)
        {
            Icons[game.Id] = icon;
            LoadingIcons.Remove(game.Id);
        }
        return icon;
    }

    /// <summary>Drops a game's cached icon (file and memory), so it's read again from its new source.</summary>
    public static void ForgetIcon(Game game)
    {
        DeleteQuietly(CachedFile(game, ArtKind.Cover, "_icon2.png"));
        lock (Sync)
        {
            Icons.Remove(game.Id);
            IconImages.Remove(game.Id);
        }
        game.IconChanged = false;
    }

    private static void DeleteQuietly(string path)
    {
        try { File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Drops decoded images and icons (the flyout closed). Resolved file paths are kept: they're tiny.</summary>
    public static void ReleaseMemory()
    {
        lock (Sync)
        {
            Memory.Clear();
            Lru.Clear();
            Icons.Clear();
            IconImages.Clear();
        }
    }

    /// <summary>
    /// A game left the library: deletes its cached art, icon and "missing" markers from our cache folder. Its
    /// custom cover copy is deleted only with <paramref name="customToo"/> (otherwise it's kept with the game's
    /// remembered settings). Only our own files, by name; never a path from the library.
    /// </summary>
    public static void Forget(Game game, bool customToo)
    {
        Invalidate(game);
        lock (Sync)
        {
            Icons.Remove(game.Id);
            IconImages.Remove(game.Id);
        }
        foreach (var kind in new[] { ArtKind.Cover, ArtKind.Hero, ArtKind.Logo })
        {
            DeleteQuietly(CachedFile(game, kind, ImageExt(kind)));
            DeleteQuietly(CachedFile(game, kind, ".none"));
        }
        DeleteQuietly(CachedFile(game, ArtKind.Cover, "_icon.png"));
        DeleteQuietly(CachedFile(game, ArtKind.Cover, "_icon2.png"));
        if (customToo) DeleteOwnCopy(game.CustomCover, except: null);
    }

    /// <summary>Forgets resolved paths and decoded images for a game, e.g. after its custom cover changed.</summary>
    public static void Invalidate(Game game)
    {
        lock (Sync)
        {
            Resolved.Remove(ResolvedKey(game, ArtKind.Cover));
            Resolved.Remove(ResolvedKey(game, ArtKind.Hero));
            Resolved.Remove(ResolvedKey(game, ArtKind.Logo));
            _generation++;
            var prefix = game.Id + "|";
            for (var node = Lru.First; node is not null;)
            {
                var next = node.Next;
                if (node.Value.Key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    Memory.Remove(node.Value.Key);
                    Lru.Remove(node);
                }
                node = next;
            }
        }
    }

    /// <summary>
    /// A Steam artwork entry changed in Settings › Advanced (spec §14): forget where Steam pictures were found and
    /// the markers for pictures the CDN didn't have, so they're looked for again. Pictures already copied into our
    /// cache and decoded images stay. Deletes only our own ".none" markers in our covers folder.
    /// </summary>
    public static void ForgetSteamLookups()
    {
        lock (Sync)
        {
            SteamLibraryCache = new(SourceDefaults.SteamArtFolder);
            foreach (var key in Resolved.Keys.Where(k => k.StartsWith("steam:", StringComparison.Ordinal)).ToList())
                Resolved.Remove(key);
            _generation++;
        }
        try
        {
            if (!Directory.Exists(CoversDir)) return;
            foreach (var marker in Directory.EnumerateFiles(CoversDir, "steam_*.none"))
            {
                var info = new FileInfo(marker);
                if (info.LinkTarget is null && info.Length == 0 &&
                    string.Equals(info.DirectoryName, Path.GetFullPath(CoversDir), StringComparison.OrdinalIgnoreCase))
                    info.Delete();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Whether a picture of this kind is on this PC already (the user's custom cover, our cache or Steam's
    /// artwork folder), without downloading. For the "Check" button in Settings › Advanced.
    /// </summary>
    public static bool HasLocal(Game game, ArtKind kind) => FindLocal(game, kind) is not null;

    private static string Key(Game game, int pixelWidth, ArtKind kind) => game.Id + "|" + kind + "|" + pixelWidth;
    private static string ResolvedKey(Game game, ArtKind kind) => game.Id + "|" + kind;

    private static void Remember(string key, ImageSource image)
    {
        lock (Sync)
        {
            if (Memory.TryGetValue(key, out var existing)) Lru.Remove(existing);
            var node = Lru.AddFirst((key, image));
            Memory[key] = node;
            while (Lru.Count > MemoryEntries)
            {
                Memory.Remove(Lru.Last!.Value.Key);
                Lru.RemoveLast();
            }
        }
    }

    private static string SafeId(Game game) => string.Join("_", game.Id.Split(Path.GetInvalidFileNameChars()));

    private static string CachedFile(Game game, ArtKind kind, string ext) =>
        Path.Combine(CoversDir, SafeId(game) + kind switch { ArtKind.Hero => "_hero", ArtKind.Logo => "_logo", _ => "" } + ext);

    /// <summary>Our cached copy's extension: logos keep their transparency.</summary>
    private static string ImageExt(ArtKind kind) => kind == ArtKind.Logo ? ".png" : ".jpg";

    /// <summary>Steam's file names for the kind, tried in order (SourceDefaults); the first is the one downloaded.</summary>
    private static string[] SteamFileNames(ArtKind kind) => kind switch
    {
        ArtKind.Hero => [SourceDefaults.SteamBannerFile()],
        ArtKind.Logo => [SourceDefaults.SteamLogoFile()],
        _ => SourceDefaults.SteamCoverFiles(),
    };

    private static string? FindLocal(Game game, ArtKind kind)
    {
        if (kind == ArtKind.Cover && !string.IsNullOrEmpty(game.CustomCover) && File.Exists(game.CustomCover))
            return game.CustomCover;

        var cached = CachedFile(game, kind, ImageExt(kind));
        if (File.Exists(cached)) return cached;

        if (game.Source == GameSource.Steam && game.StoreId is { } appId && SteamLibraryCache.Value is { } lc)
            return FindSteamLocal(lc, appId, kind);
        return null;
    }

    private static string? FindSteamLocal(string libraryCache, string appId, ArtKind kind)
    {
        try
        {
            // Each name is looked for everywhere before the next one: librarycache\{appid}\{name}, the older
            // librarycache\{appid}_{name}, then the hashed subfolders newer clients use (where the portrait art may
            // be library_capsule.jpg, the second cover name).
            var dir = Path.Combine(libraryCache, appId);
            List<string>? subs = null;
            foreach (var name in SteamFileNames(kind))
            {
                var direct = Path.Combine(dir, name);
                if (File.Exists(direct)) return direct;
                var legacy = Path.Combine(libraryCache, appId + "_" + name);
                if (File.Exists(legacy)) return legacy;
                subs ??= Directory.Exists(dir) ? [.. Directory.EnumerateDirectories(dir)] : [];
                foreach (var sub in subs)
                {
                    var file = Path.Combine(sub, name);
                    if (File.Exists(file)) return file;
                }
            }
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Downloads the game's art from Steam once into our cache. Retry: it couldn't be reached (offline, a timeout, a
    /// server error), so it's looked for again the next time the panel opens; a 404 is remembered for good (".none").
    /// </summary>
    private static async Task<(string? Path, bool Retry)> DownloadAsync(Game game, ArtKind kind)
    {
        if (game.Source != GameSource.Steam || game.StoreId is not { } appId) return (null, false);

        var target = CachedFile(game, kind, ImageExt(kind));
        var missingMarker = CachedFile(game, kind, ".none");
        if (File.Exists(missingMarker)) return (null, false); // the CDN had nothing last time; don't ask again

        await DownloadGate.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (Sync)
                if (_offline) return (null, true); // an earlier one just failed: wait for the next open
            // The appid is digits only (checked when the game was read) and the file name a checked image name.
            if (!appId.All(char.IsAsciiDigit)) return (null, false);
            var url = SourceDefaults.SteamDownloadUrl(appId, SteamFileNames(kind)[0]);
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return (null, false);
            using var response = await Http.Value.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            Directory.CreateDirectory(CoversDir);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                await File.WriteAllBytesAsync(missingMarker, []).ConfigureAwait(false);
                return (null, false);
            }
            if (!response.IsSuccessStatusCode) return (null, true); // a server problem: try again later
            if (response.Content.Headers.ContentLength > MaxDownloadBytes) return (null, false);

            var tmp = target + ".tmp";
            bool complete;
            await using (var file = File.Create(tmp))
            await using (var body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                complete = await CopyLimitedAsync(body, file, MaxDownloadBytes).ConfigureAwait(false);
            if (!complete)
            {
                File.Delete(tmp);
                return (null, false);
            }
            File.Move(tmp, target, overwrite: true);
            return (target, false);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            lock (Sync) _offline = true; // offline or no internet: the others wait for the next open too
            return (null, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (null, true); // our cache folder was busy: try again next open
        }
        finally
        {
            DownloadGate.Release();
        }
    }

    /// <summary>Copies at most <paramref name="limit"/> bytes; false if the source had more.</summary>
    private static async Task<bool> CopyLimitedAsync(Stream source, Stream target, long limit)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > limit) return false;
            await target.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
        }
        return true;
    }

}
