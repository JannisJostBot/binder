using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Binder.Library;
using Binder.Models;

namespace Binder.Services;

/// <summary>What a check found (spec §25, "In the app").</summary>
public enum UpdateCheckOutcome
{
    /// <summary>A newer, verified release that the user hasn't skipped.</summary>
    Found,
    /// <summary>Nothing newer (or the newest is the skipped version, or no release has a manifest yet).</summary>
    UpToDate,
    /// <summary>No connection, a timeout or an HTTP error.</summary>
    NoConnection,
    /// <summary>A bad signature, an unparsable manifest, an old or future <c>issued</c>, a refused address. Logged.</summary>
    Invalid,
}

public sealed record UpdateCheckResult(UpdateCheckOutcome Outcome, VerifiedRelease? Release);

public enum UpdateDownloadOutcome { Done, Cancelled, NoConnection, NotVerified }

public enum UpdateStartOutcome { Started, NotVerified, Failed }

/// <summary>
/// Updates from GitHub (spec §25): the network, the files and the setup's start. The rules (signature first, then
/// the strict manifest, no downgrades, GitHub's release hosts only) are <see cref="UpdateManifest"/>'s. Nothing here
/// runs on a timer: a check is one pair of small requests when the panel opens (at most once a day) or from
/// Settings; the download only after Update or with "Install automatically". Never elevates.
/// </summary>
public static class Updater
{
    /// <summary>The only folder the updater writes to or deletes in: <c>%LOCALAPPDATA%\Binder\updates\</c>.</summary>
    public static string Folder => Path.Combine(Storage.CacheDir, "updates");

    private static string ManifestPath => Path.Combine(Folder, "release.json");
    private static string SignaturePath => Path.Combine(Folder, "release.json.sig");

    public static readonly TimeSpan CheckEvery = TimeSpan.FromHours(24);
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(15);
    private const int MaxRedirects = 5;

    /// <summary>"0.1.4": the installed widget's version (Directory.Build.props).</summary>
    public static Version Installed { get; } = Normalize(typeof(Updater).Assembly.GetName().Version);

    private static Version Normalize(Version? v) => v is null ? new Version(0, 0, 0) : new Version(v.Major, v.Minor, Math.Max(v.Build, 0));

    /// <summary>Whether a check is due: the user allows it and the last one was over a day ago (or never ran).</summary>
    public static bool CheckDue(Settings settings, DateTimeOffset now) =>
        settings.Updates != UpdateMode.Off &&
        (settings.UpdateCheckedAt is not { } last || now - last >= CheckEvery || last > now + CheckEvery);

    // ---- The check ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Fetches <c>release.json</c> and its signature, verifies, judges against what's installed and what was accepted
    /// before. A verified manifest is kept in <see cref="Folder"/> (read back and verified again later). The caller
    /// stores <see cref="Settings.UpdateCheckedAt"/> and, from the result, <see cref="Settings.UpdateIssued"/>.
    /// <paramref name="lastIssued"/> and <paramref name="skipped"/> are those settings' values, read on the UI thread.
    /// </summary>
    public static async Task<UpdateCheckResult> CheckAsync(DateTimeOffset? lastIssued, string? skipped, CancellationToken cancel)
    {
        byte[]? manifest, signature;
        try
        {
            using var http = CreateClient(CheckTimeout);
            manifest = await GetSmallAsync(http, UpdateManifest.ManifestUri, UpdateManifest.MaxManifestBytes, cancel).ConfigureAwait(false);
            if (manifest is null) return new(UpdateCheckOutcome.UpToDate, null); // no release with a manifest yet
            signature = await GetSmallAsync(http, UpdateManifest.SignatureUri, UpdateManifest.MaxSignatureBytes, cancel).ConfigureAwait(false);
            if (signature is null) return Invalid("the release has a manifest but no signature");
        }
        catch (RefusedException e)
        {
            return Invalid(e.Message);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException && !cancel.IsCancellationRequested)
        {
            return new(UpdateCheckOutcome.NoConnection, null);
        }

        VerifiedRelease? release;
        try { release = UpdateManifest.Verify(manifest, signature, Services.UpdateKeys.All); }
        catch (FormatException e) { return Invalid("signature or manifest malformed: " + e.Message); }
        catch (Exception e) when (e is ArgumentException or System.Security.Cryptography.CryptographicException)
        {
            return Invalid("a compiled-in key can't be read: " + e.Message);
        }
        if (release is null) return Invalid("no key verifies the signature");

        var verdict = UpdateManifest.Judge(release, Installed, lastIssued, DateTimeOffset.UtcNow,
            skipped is { } s ? UpdateManifest.ParseVersion(s) : null);
        switch (verdict)
        {
            case UpdateVerdict.Stale:
                return Invalid($"manifest {UpdateManifest.Text(release.Manifest.Version)} issued before the last one accepted");
            case UpdateVerdict.FromFuture:
                return Invalid($"manifest {UpdateManifest.Text(release.Manifest.Version)} issued over a day in the future");
            case UpdateVerdict.NotNewer or UpdateVerdict.Skipped:
                return new(UpdateCheckOutcome.UpToDate, release);
        }
        Save(manifest, signature);
        return new(UpdateCheckOutcome.Found, release);
    }

    private static UpdateCheckResult Invalid(string why)
    {
        ErrorLog.Note("update check: " + why);
        return new(UpdateCheckOutcome.Invalid, null);
    }

    /// <summary>
    /// The verified manifest kept from an earlier check, verified again (null if missing, changed or no longer
    /// newer than what's installed). Used when the panel opens within the 24 hours and after <c>--updated</c>.
    /// </summary>
    public static VerifiedRelease? LoadSaved(bool newerOnly = true)
    {
        try
        {
            if (!UsableFolder()) return null;
            var manifest = ReadCapped(ManifestPath, UpdateManifest.MaxManifestBytes);
            var signature = ReadCapped(SignaturePath, UpdateManifest.MaxSignatureBytes);
            if (manifest is null || signature is null) return null;
            var release = UpdateManifest.Verify(manifest, signature, Services.UpdateKeys.All);
            if (release is null) return null;
            if (newerOnly && release.Manifest.Version <= Installed) return null;
            return release;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException or ArgumentException
                                       or System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    private static byte[]? ReadCapped(string path, int cap)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > cap || info.Attributes.HasFlag(FileAttributes.ReparsePoint)) return null;
        return File.ReadAllBytes(path);
    }

    private static void Save(byte[] manifest, byte[] signature)
    {
        try
        {
            if (!EnsureFolder()) return;
            File.WriteAllBytes(ManifestPath, manifest);
            File.WriteAllBytes(SignaturePath, signature);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write(e, "update manifest save");
        }
    }

    // ---- The download ------------------------------------------------------------------------------------------

    /// <summary>The downloaded setup's path if it's there (it's hashed again before it starts).</summary>
    public static string SetupPath(UpdateManifest manifest) => Path.Combine(Folder, UpdateManifest.SetupFileName(manifest.Version));

    public static bool IsDownloaded(UpdateManifest manifest) => UsableFolder() && File.Exists(SetupPath(manifest));

    /// <summary>
    /// Downloads the setup to <c>BinderSetup-&lt;version&gt;.exe.part</c>, hashed while it streams and stopped past the
    /// manifest's size, then compared and renamed. Cancelled or failed: the part file is deleted (the next try starts
    /// over). <paramref name="progress"/> gets the bytes received so far.
    /// </summary>
    public static async Task<UpdateDownloadOutcome> DownloadAsync(UpdateManifest manifest, IProgress<long>? progress, CancellationToken cancel)
    {
        if (!EnsureFolder()) return UpdateDownloadOutcome.NoConnection;
        var target = SetupPath(manifest);
        var part = target + ".part";
        try
        {
            using var http = CreateClient(DownloadTimeout);
            using var response = await SendAsync(http, UpdateManifest.SetupUri(manifest.Version), cancel).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return UpdateDownloadOutcome.NoConnection;
            if (response.Content.Headers.ContentLength is { } length && length != manifest.SetupSize)
                return NotVerified(part, $"setup {UpdateManifest.Text(manifest.Version)} is {length} bytes, expected {manifest.SetupSize}");

            bool matches;
            using (var check = new SetupDownloadCheck(manifest.SetupSize, manifest.SetupSha256))
            {
                await using (var file = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                await using (var body = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false))
                {
                    var buffer = new byte[81920];
                    int read;
                    while ((read = await body.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0)
                    {
                        if (!check.Append(buffer.AsSpan(0, read)))
                            break; // longer than the manifest says: Matches() fails below
                        await file.WriteAsync(buffer.AsMemory(0, read), cancel).ConfigureAwait(false);
                        progress?.Report(check.Received);
                    }
                }
                matches = check.Matches();
            }
            if (!matches) return NotVerified(part, $"setup {UpdateManifest.Text(manifest.Version)} doesn't match the manifest's size and hash");
            File.Move(part, target, overwrite: true);
            return UpdateDownloadOutcome.Done;
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            TryDelete(part);
            return UpdateDownloadOutcome.Cancelled;
        }
        catch (RefusedException e)
        {
            TryDelete(part);
            ErrorLog.Note("update download: " + e.Message);
            return UpdateDownloadOutcome.NoConnection;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            TryDelete(part);
            return UpdateDownloadOutcome.NoConnection;
        }
    }

    private static UpdateDownloadOutcome NotVerified(string part, string why)
    {
        TryDelete(part);
        ErrorLog.Note("update download: " + why + "; deleted");
        return UpdateDownloadOutcome.NotVerified;
    }

    // ---- Starting the setup ------------------------------------------------------------------------------------

    /// <summary>
    /// Whether installing has to wait: a watcher is following a game, or Game mode's changes (either journal) haven't
    /// been put back yet.
    /// </summary>
    public static bool MustWait()
    {
        if (GameModeService.Pending) return true;
        var watchers = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Launcher.WatcherPath));
        foreach (var p in watchers) p.Dispose();
        return watchers.Length > 0;
    }

    /// <summary>
    /// Starts the downloaded setup with <c>/S /update</c>. The file is opened with read sharing only (no write, no
    /// delete), hashed through that handle and kept open until the process has started, so nothing can swap it in
    /// between. A file that doesn't match is deleted. The caller exits right after <see cref="UpdateStartOutcome.Started"/>.
    /// </summary>
    public static UpdateStartOutcome Start(UpdateManifest manifest)
    {
        var path = SetupPath(manifest);
        bool delete = false;
        try
        {
            if (!UsableFolder() || new FileInfo(path) is not { Exists: true } info || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                return UpdateStartOutcome.NotVerified;
            using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920);
            using (var check = new SetupDownloadCheck(manifest.SetupSize, manifest.SetupSha256))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = held.Read(buffer)) > 0)
                    if (!check.Append(buffer.AsSpan(0, read))) break;
                if (!check.Matches())
                {
                    delete = true;
                    ErrorLog.Note($"update start: setup {UpdateManifest.Text(manifest.Version)} changed since its download; deleted");
                    return UpdateStartOutcome.NotVerified;
                }
            }
            var start = new ProcessStartInfo(path) { UseShellExecute = false, WorkingDirectory = Folder };
            start.ArgumentList.Add("/S");
            start.ArgumentList.Add("/update");
            using var process = Process.Start(start);
            return process is null ? UpdateStartOutcome.Failed : UpdateStartOutcome.Started;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            ErrorLog.Write(e, "update start");
            return UpdateStartOutcome.Failed;
        }
        finally
        {
            if (delete) TryDelete(path); // after the handle is closed
        }
    }

    /// <summary>After <c>--updated</c>: empties the updates folder (only plain files directly in it).</summary>
    public static void EmptyFolder()
    {
        try
        {
            if (!UsableFolder()) return;
            foreach (var file in new DirectoryInfo(Folder).EnumerateFiles())
                TryDelete(file.FullName);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    // ---- Files -------------------------------------------------------------------------------------------------

    /// <summary>The folder exists and is a real folder, not a link to somewhere else.</summary>
    private static bool UsableFolder()
    {
        var info = new DirectoryInfo(Folder);
        return info.Exists && !info.Attributes.HasFlag(FileAttributes.ReparsePoint);
    }

    private static bool EnsureFolder()
    {
        try
        {
            if (!Directory.Exists(Folder)) Directory.CreateDirectory(Folder);
            return UsableFolder();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        // Only inside our updates folder, whatever the name says.
        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Path.GetFullPath(Folder), StringComparison.OrdinalIgnoreCase)) return;
        try { File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    // ---- HTTP ----------------------------------------------------------------------------------------------------

    private sealed class RefusedException(string message) : Exception(message);

    /// <summary>Made for one check or download and released after it. No cookies, no automatic redirects.</summary>
    private static HttpClient CreateClient(TimeSpan timeout)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false, // followed by hand, through IsAllowedAddress
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = CheckTimeout,
        };
        var http = new HttpClient(handler, disposeHandler: true) { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Binder", UpdateManifest.Text(Installed)));
        return http;
    }

    /// <summary>A GET that follows redirects only to <see cref="UpdateManifest.IsAllowedAddress"/>.</summary>
    private static async Task<HttpResponseMessage> SendAsync(HttpClient http, Uri uri, CancellationToken cancel)
    {
        for (int hop = 0; hop <= MaxRedirects; hop++)
        {
            if (!UpdateManifest.IsAllowedAddress(uri)) throw new RefusedException($"refused address {uri.Scheme}://{uri.IdnHost}");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                response.Dispose();
                continue;
            }
            return response;
        }
        throw new RefusedException("too many redirects");
    }

    /// <summary>A small file, at most <paramref name="cap"/> bytes actually read; null for 404.</summary>
    private static async Task<byte[]?> GetSmallAsync(HttpClient http, Uri uri, int cap, CancellationToken cancel)
    {
        using var response = await SendAsync(http, uri, cancel).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"HTTP {(int)response.StatusCode}");
        if (response.Content.Headers.ContentLength > cap) throw new RefusedException($"{uri.AbsolutePath} over {cap} bytes");
        await using var body = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
        var buffer = new byte[cap + 1];
        int total = 0, read;
        while (total < buffer.Length && (read = await body.ReadAsync(buffer.AsMemory(total), cancel).ConfigureAwait(false)) > 0)
            total += read;
        if (total > cap) throw new RefusedException($"{uri.AbsolutePath} over {cap} bytes");
        return buffer.AsSpan(0, total).ToArray();
    }
}
