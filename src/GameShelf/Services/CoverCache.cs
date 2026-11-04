using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using GameShelf.Library;
using GameShelf.Models;
using GameShelf.Views;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace GameShelf.Services;

/// <summary>Which artwork to load: the 600×900 portrait cover, or the wide hero banner for the detail view.</summary>
public enum ArtKind { Cover, Hero }

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
    private static readonly Lazy<string?> SteamLibraryCache = new(() =>
        SteamSource.FindSteamPath() is { } steam ? Path.Combine(steam, "appcache", "librarycache") : null);
    // Created on the first download only (most starts download nothing), and only once even when two run at once.
    private static readonly Lazy<HttpClient> Http = new(() => new HttpClient { Timeout = TimeSpan.FromSeconds(20) });

    // Resolved file path per game id (null = no cover). Avoids re-probing the disk on every realization.
    private static readonly Dictionary<string, string?> Resolved = [];
    private static readonly Dictionary<string, LinkedListNode<(string Key, ImageSource Image)>> Memory = [];
    private static readonly LinkedList<(string Key, ImageSource Image)> Lru = new();
    private static readonly object Sync = new();

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
            var task = Task.Run(async () =>
            {
                var path = FindLocal(game, kind) ?? await DownloadAsync(game, kind).ConfigureAwait(false);
                lock (Sync)
                {
                    Resolved[key] = path;
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

    /// <summary>Forgets resolved paths and decoded images for a game, e.g. after its custom cover changed.</summary>
    public static void Invalidate(Game game)
    {
        lock (Sync)
        {
            Resolved.Remove(ResolvedKey(game, ArtKind.Cover));
            Resolved.Remove(ResolvedKey(game, ArtKind.Hero));
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
        Path.Combine(CoversDir, SafeId(game) + (kind == ArtKind.Hero ? "_hero" : "") + ext);

    private static string SteamFileName(ArtKind kind) => kind == ArtKind.Hero ? "library_hero.jpg" : "library_600x900.jpg";

    private static string? FindLocal(Game game, ArtKind kind)
    {
        if (kind == ArtKind.Cover && !string.IsNullOrEmpty(game.CustomCover) && File.Exists(game.CustomCover))
            return game.CustomCover;

        var cached = CachedFile(game, kind, ".jpg");
        if (File.Exists(cached)) return cached;

        if (game.Source == GameSource.Steam && game.StoreId is { } appId && SteamLibraryCache.Value is { } lc)
            return FindSteamLocal(lc, appId, kind);
        return null;
    }

    private static string? FindSteamLocal(string libraryCache, string appId, ArtKind kind)
    {
        try
        {
            var name = SteamFileName(kind);
            var dir = Path.Combine(libraryCache, appId);
            var direct = Path.Combine(dir, name);
            if (File.Exists(direct)) return direct;

            var legacy = Path.Combine(libraryCache, appId + "_" + name);
            if (File.Exists(legacy)) return legacy;

            // Newer clients keep assets in hashed subfolders, and may name the portrait art library_capsule.jpg.
            if (!Directory.Exists(dir)) return null;
            string? capsule = null;
            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                var file = Path.Combine(sub, name);
                if (File.Exists(file)) return file;
                if (kind != ArtKind.Cover) continue;
                var cap = Path.Combine(sub, "library_capsule.jpg");
                if (capsule is null && File.Exists(cap)) capsule = cap;
            }
            return capsule;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static async Task<string?> DownloadAsync(Game game, ArtKind kind)
    {
        if (game.Source != GameSource.Steam || game.StoreId is not { } appId) return null;

        var target = CachedFile(game, kind, ".jpg");
        var missingMarker = CachedFile(game, kind, ".none");
        if (File.Exists(missingMarker)) return null; // the CDN had nothing last time; don't ask again

        await DownloadGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var url = $"https://shared.steamstatic.com/store_item_assets/steam/apps/{appId}/{SteamFileName(kind)}";
            using var response = await Http.Value.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            Directory.CreateDirectory(CoversDir);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                await File.WriteAllBytesAsync(missingMarker, []).ConfigureAwait(false);
                return null;
            }
            if (!response.IsSuccessStatusCode) return null;
            if (response.Content.Headers.ContentLength > MaxDownloadBytes) return null;

            var tmp = target + ".tmp";
            bool complete;
            await using (var file = File.Create(tmp))
            await using (var body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                complete = await CopyLimitedAsync(body, file, MaxDownloadBytes).ConfigureAwait(false);
            if (!complete)
            {
                File.Delete(tmp);
                return null;
            }
            File.Move(tmp, target, overwrite: true);
            return target;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            return null; // offline or similar: try again next start
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
