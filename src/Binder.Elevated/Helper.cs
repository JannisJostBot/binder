using System.Text.Json;
using Binder.Shared;
using Microsoft.Win32;

namespace Binder.Elevated;

/// <summary>
/// Apply and restore (spec §11): services, network priority (QoS) and the Windows Update limit. One run at a time
/// (a file lock in the admin-only ProgramData folder); the journal there is written before each change, and a
/// restore puts back, in reverse order, only what is still the way we set it.
/// </summary>
internal static class Helper
{
    private const long MaxLogBytes = 256 * 1024;
    private const int DscpGame = 46;

    // ---- Apply --------------------------------------------------------------------------------------------

    public static int Apply(string sid)
    {
        if (ReadRequest() is not { } request)
        {
            Log("Apply: no valid request");
            return 2;
        }
        using var gate = Lock();
        RestoreResult? result = null;
        try
        {
            if (!DataFolder.IsOurs())
            {
                Log("Apply: the ProgramData folder isn't ours, nothing changed");
                return 4;
            }
            if (!gate.Held)
            {
                Log("Apply: another run didn't finish in time, nothing changed");
                return 6;
            }
            var journal = Journal.Load();
            if (journal is not null && journal.Sid != sid)
            {
                Log("Apply: another user's changes are still applied, nothing changed");
                return 5;
            }
            if (journal is not null && journal.Session != request.Session)
            {
                Log("Apply: putting back changes left from an earlier session");
                Restore(journal);
                journal = null;
            }
            journal ??= new Journal { Sid = sid, Session = request.Session, Started = DateTimeOffset.Now };
            journal.Save(); // before the first change

            // What this run changed, for the "Game mode is on" toast: each service, network priority, the upload
            // limit and the Windows Update limit count as one change each.
            var tally = new Tally();
            foreach (var id in request.Services)
            {
                if (journal.Services.Contains(id)) continue;
                tally.Add(Services.Pause(id, journal), ElevatedPart.ServiceFor(id)?.Name ?? id);
            }
            if (request.Smooth.Count > 0 || request.Throttle.Count > 0)
            {
                var (smooth, uploads) = ApplyQos(request, journal);
                tally.Add(smooth, "Network priority");
                tally.Add(uploads, "Upload limit");
            }
            if (request.LimitUpdates && journal.Updates is null) tally.Add(ApplyUpdateLimit(journal), "Windows Update limit");
            result = tally.Result();

            if (journal.IsEmpty) Journal.Delete(); // nothing was changed: nothing to put back
            return 0;
        }
        finally
        {
            Finish(sid, request.Event, result);
        }
    }

    /// <summary>Counts what an apply changed and names what it couldn't.</summary>
    internal sealed class Tally
    {
        private int _applied;
        private readonly List<string> _failed = [];

        /// <summary>True: changed. False: couldn't be changed. Null: left alone (nothing to do, or not safe to).</summary>
        public void Add(bool? outcome, string name)
        {
            if (outcome == true) _applied++;
            else if (outcome == false) _failed.Add(name);
        }

        public RestoreResult Result() => new(_applied, _applied + _failed.Count, [.. _failed]);
    }

    /// <summary>
    /// Adds the policies not added yet. For each group (kept smooth, slowed uploads): true if any was added, false
    /// if some failed and none was added, null if there was nothing new to add.
    /// </summary>
    private static (bool? Smooth, bool? Uploads) ApplyQos(ElevatedRequest request, Journal journal)
    {
        using var qos = Qos.Connect(out int hr);
        if (qos is null)
        {
            Log($"Network: QoS isn't available (0x{hr:X8}), left as is");
            return (request.Smooth.Count > 0 ? false : null, request.Throttle.Count > 0 ? false : null);
        }
        bool? smooth = null, uploads = null;
        foreach (var exe in request.Smooth) Merge(ref smooth, Add(Qos.Prefix + "smooth " + exe, exe, DscpGame, null));
        ulong limit = (ulong)request.UploadLimitMbps * 1_000_000;
        // Never a Windows service, security software or another account's program (ProtectedApps).
        var guarded = ProtectedApps.Among(request.Throttle, journal.Sid);
        foreach (var exe in request.Throttle)
        {
            if (guarded.Contains(exe))
            {
                Log($"Network: {exe} is a service, security software or not yours; its uploads are left alone");
                continue;
            }
            Merge(ref uploads, Add(Qos.Prefix + "upload " + exe, exe, null, limit));
        }
        return (smooth, uploads);

        static void Merge(ref bool? group, bool? one)
        {
            if (one == true) group = true;
            else if (one == false && group is null) group = false;
        }

        bool? Add(string name, string exe, int? dscp, ulong? bps)
        {
            if (journal.Policies.Contains(name, StringComparer.OrdinalIgnoreCase)) return null;
            journal.Policies.Add(name);
            journal.Save();
            int result = qos.Create(name, exe, dscp, bps);
            if (result >= 0)
            {
                Log(dscp is not null ? $"Network: {exe} first (DSCP {dscp})" : $"Network: {exe} uploads at most {bps / 1_000_000} Mbit/s");
                return true;
            }
            journal.Policies.Remove(name);
            journal.Save();
            Log($"Network: couldn't add the policy for {exe} (0x{result:X8})");
            return false;
        }
    }

    // Delivery Optimization's bandwidth policy for background downloads (Windows Update): a share in percent.
    private const string UpdatePolicyKey = @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization";
    private const string UpdatePolicyValue = "DOPercentageMaxBackgroundBandwidth";

    /// <summary>True when the limit was set, null when it was left as is.</summary>
    private static bool? ApplyUpdateLimit(Journal journal)
    {
        using var existing = Registry.LocalMachine.OpenSubKey(UpdatePolicyKey);
        var before = existing?.GetValue(UpdatePolicyValue);
        if (before is not null and not int)
        {
            Log("Windows Update: its bandwidth policy has an unexpected type, left as is");
            return null;
        }
        if (before is int current && current is > 0 and <= ElevatedPart.UpdatePercent)
        {
            Log($"Windows Update: already limited to {current}%, left as is");
            return null;
        }
        journal.Updates = new UpdateChange(before as int?, ElevatedPart.UpdatePercent, KeyCreated: existing is null);
        journal.Save();
        using var key = Registry.LocalMachine.CreateSubKey(UpdatePolicyKey, writable: true);
        key.SetValue(UpdatePolicyValue, ElevatedPart.UpdatePercent, RegistryValueKind.DWord);
        Log($"Windows Update: background downloads limited to {ElevatedPart.UpdatePercent}% (was {(before is int b ? b + "%" : "not set")})");
        return true;
    }

    // ---- Restore ------------------------------------------------------------------------------------------

    /// <summary>The Restore task: from the watcher after the game, or at logon (a session that didn't end normally).</summary>
    public static int RestoreTask(string sid)
    {
        var request = ReadRequest();
        using var gate = Lock();
        RestoreResult? result = null;
        try
        {
            // Only a journal from our admin-only folder is acted on.
            if (!DataFolder.IsOurs()) return 4;
            var journal = Journal.Load();
            if (journal is null) return 0;
            if (journal.Sid != sid)
            {
                Log("Restore: the changes are another user's, left for them");
                return 5;
            }
            result = Restore(journal);
            return result.Success ? 0 : 1;
        }
        finally
        {
            if (request is not null) Finish(sid, request.Event, result ?? new RestoreResult(0, 0, []));
        }
    }

    /// <summary>Reverse order of applying: the Windows Update limit, network, then the services. Then deletes the journal.</summary>
    public static RestoreResult Restore(Journal journal)
    {
        int total = 0, restored = 0;
        var failed = new List<string>();

        if (journal.Updates is { } updates)
        {
            total++;
            if (RestoreUpdateLimit(updates)) restored++;
            else failed.Add("Windows Update limit");
        }

        if (journal.Policies.Count > 0)
        {
            total++;
            if (RestoreQos(journal.Policies)) restored++;
            else failed.Add("Network priority");
        }

        for (int i = journal.Services.Count - 1; i >= 0; i--)
        {
            total++;
            var id = journal.Services[i];
            if (Services.Resume(id)) restored++;
            else failed.Add(ElevatedPart.ServiceFor(id)?.Name ?? id);
        }

        Log($"Restored {restored} of {total}" + (failed.Count > 0 ? ": " + string.Join(", ", failed) + " not put back" : ""));
        // Also when something couldn't be put back: that is logged and shown, and retrying it at every start wouldn't help.
        Journal.Delete();
        return new RestoreResult(restored, total, failed);
    }

    private static bool RestoreUpdateLimit(UpdateChange change)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(UpdatePolicyKey, writable: true);
            if (key?.GetValue(UpdatePolicyValue) is not int now || now != change.Set)
            {
                Log("Windows Update: its limit was changed meanwhile, left as is");
                return true;
            }
            if (change.Before is { } before) key.SetValue(UpdatePolicyValue, before, RegistryValueKind.DWord);
            else key.DeleteValue(UpdatePolicyValue, throwOnMissingValue: false);
            bool empty = key.ValueCount == 0 && key.SubKeyCount == 0;
            key.Dispose();
            if (change.KeyCreated && empty) Registry.LocalMachine.DeleteSubKey(UpdatePolicyKey, throwOnMissingSubKey: false);
            Log($"Windows Update: limit back to {(change.Before is int b ? b + "%" : "not set")}");
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log("Windows Update: couldn't put its limit back: " + e.Message);
            return false;
        }
    }

    private static bool RestoreQos(List<string> names)
    {
        using var qos = Qos.Connect(out int hr);
        if (qos is null)
        {
            Log($"Network: QoS isn't available (0x{hr:X8}); the policies go away with the next restart");
            return false;
        }
        // Ours from the journal, and any other left over with our prefix. The ActiveStore also holds the user's
        // own policies (Group Policy ones show up there too), which never have it.
        var ours = qos.Ours();
        if (ours is null)
        {
            Log("Network: couldn't list the policies");
            return false;
        }
        int left = 0;
        foreach (var (name, path) in ours)
        {
            int result = qos.Delete(path);
            if (result < 0)
            {
                left++;
                Log($"Network: couldn't remove \"{name}\" (0x{result:X8})");
            }
        }
        if (left == 0) Log($"Network: {ours.Count} {(ours.Count == 1 ? "policy" : "policies")} removed");
        return left == 0;
    }

    // ---- Request, result, event ----------------------------------------------------------------------------

    /// <summary>The watcher's request from the user's own folder: untrusted, size-capped, checked by <see cref="ElevatedRequest.Parse"/>.</summary>
    private static ElevatedRequest? ReadRequest()
    {
        try
        {
            var info = new FileInfo(GameModeInfo.RequestPath);
            if (!info.Exists || info.Length > ElevatedRequest.MaxBytes || info.Attributes.HasFlag(FileAttributes.ReparsePoint)) return null;
            // The size is capped on what is actually read, not only on the check above: the file may change in between.
            using var stream = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.None);
            var buffer = new byte[ElevatedRequest.MaxBytes + 1];
            int read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            if (read > ElevatedRequest.MaxBytes) return null;
            return ElevatedRequest.Parse(System.Text.Encoding.UTF8.GetString(buffer, 0, read));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Writes the result for this user (restores only) and sets the watcher's event.</summary>
    private static void Finish(string sid, string eventId, RestoreResult? result)
    {
        if (result is not null && DataFolder.IsOurs())
        {
            try
            {
                File.WriteAllLines(ElevatedPart.ResultPath(sid), [eventId, result.Encode()]);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log("Couldn't write the result: " + e.Message);
            }
        }
        var handle = Native.OpenEventW(Native.EVENT_MODIFY_STATE, false, ElevatedPart.EventPrefix + eventId);
        if (handle == IntPtr.Zero) return; // the watcher stopped waiting
        Native.SetEvent(handle);
        Native.CloseHandle(handle);
    }

    /// <summary>
    /// One run at a time: an exclusive lock on a file in the admin-only folder, waited for up to 2 minutes. Not a
    /// named mutex: a user's program could create that name first and hold it. Without the lock (folder not ours,
    /// or it timed out) Apply changes nothing; a restore still goes ahead, since leaving changes applied is worse.
    /// </summary>
    private static Gate Lock()
    {
        if (!DataFolder.IsOurs()) return new Gate(null);
        var path = Path.Combine(ElevatedPart.DataDir, "helper.lock");
        long end = Environment.TickCount64 + 120_000;
        while (true)
        {
            try
            {
                return new Gate(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose));
            }
            catch (IOException) when (Environment.TickCount64 < end)
            {
                Thread.Sleep(250); // another run holds it
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log("Couldn't take the lock: " + e.Message);
                return new Gate(null);
            }
        }
    }

    private readonly record struct Gate(FileStream? File) : IDisposable
    {
        public bool Held => File is not null;
        public void Dispose() => File?.Dispose();
    }

    // ---- Log ----------------------------------------------------------------------------------------------

    /// <summary>One line in ProgramData\Binder\elevated.log, kept under ~256 KB. Only into our own folder.</summary>
    public static void Log(string text)
    {
        try
        {
            if (!DataFolder.IsOurs()) return;
            var path = ElevatedPart.LogPath;
            var info = new FileInfo(path);
            if (info.Exists && info.Length > MaxLogBytes) File.Move(path, path + ".old", overwrite: true);
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {text}\r\n");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>The Windows Update limit we set: the value before (null = not set), ours, and whether we created the key.</summary>
internal sealed record UpdateChange(int? Before, int Set, bool KeyCreated);

/// <summary>What the helper changed, flushed to disk before each change. In the admin-only folder; still checked on load.</summary>
internal sealed class Journal
{
    private const long MaxBytes = 64 * 1024;

    public string Sid { get; set; } = "";
    public string Session { get; set; } = "";
    public DateTimeOffset Started { get; set; }
    /// <summary>Item ids of the services we stopped, in order.</summary>
    public List<string> Services { get; } = [];
    /// <summary>Names of the QoS policies we created.</summary>
    public List<string> Policies { get; } = [];
    public UpdateChange? Updates { get; set; }

    public bool IsEmpty => Services.Count == 0 && Policies.Count == 0 && Updates is null;

    public void Save()
    {
        var path = ElevatedPart.JournalPath;
        var tmp = path + ".tmp";
        using (var file = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            file.Write(ToJson());
            file.Flush(flushToDisk: true);
        }
        File.Move(tmp, path, overwrite: true);
    }

    public static void Delete()
    {
        try
        {
            File.Delete(ElevatedPart.JournalPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Helper.Log("Couldn't delete the journal: " + e.Message);
        }
    }

    internal byte[] ToJson()
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>(256);
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteNumber("Version", 1);
            w.WriteString("Sid", Sid);
            w.WriteString("Session", Session);
            w.WriteString("Started", Started);
            w.WriteStartArray("Services");
            foreach (var s in Services) w.WriteStringValue(s);
            w.WriteEndArray();
            w.WriteStartArray("Policies");
            foreach (var p in Policies) w.WriteStringValue(p);
            w.WriteEndArray();
            if (Updates is { } u)
            {
                w.WriteStartObject("Updates");
                if (u.Before is int before) w.WriteNumber("Before", before);
                w.WriteNumber("Set", u.Set);
                w.WriteBoolean("KeyCreated", u.KeyCreated);
                w.WriteEndObject();
            }
            w.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>The journal on disk, or null. A damaged one is kept aside (.bad) and logged, never acted on.</summary>
    public static Journal? Load()
    {
        var path = ElevatedPart.JournalPath;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return null;
            if (info.Length > MaxBytes) throw new FormatException("too large");
            return Parse(File.ReadAllBytes(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Helper.Log("Couldn't read the journal: " + e.Message);
            return null;
        }
        catch (Exception e) when (e is FormatException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            Helper.Log("The journal is damaged, kept as journal.json.bad: " + e.Message);
            try { File.Move(path, path + ".bad", overwrite: true); }
            catch (Exception moveError) when (moveError is IOException or UnauthorizedAccessException) { }
            return null;
        }
    }

    internal static Journal Parse(byte[] json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
        var root = doc.RootElement;
        var journal = new Journal
        {
            Sid = root.GetProperty("Sid").GetString() ?? "",
            Session = root.GetProperty("Session").GetString() ?? "",
            Started = root.GetProperty("Started").GetDateTimeOffset(),
        };
        if (!ElevatedPart.IsUserSid(journal.Sid) || !ElevatedRequest.IsId(journal.Session)) throw new FormatException("bad owner");
        foreach (var s in root.GetProperty("Services").EnumerateArray())
            if (s.GetString() is { } id && ElevatedPart.ServiceFor(id) is not null && !journal.Services.Contains(id)) journal.Services.Add(id);
        foreach (var p in root.GetProperty("Policies").EnumerateArray())
            if (p.GetString() is { } name && name.StartsWith(Qos.Prefix, StringComparison.OrdinalIgnoreCase) && journal.Policies.Count < 128)
                journal.Policies.Add(name);
        if (root.TryGetProperty("Updates", out var u))
        {
            int? before = u.TryGetProperty("Before", out var b) ? b.GetInt32() : null;
            journal.Updates = new UpdateChange(before, u.GetProperty("Set").GetInt32(), u.GetProperty("KeyCreated").GetBoolean());
        }
        return journal;
    }
}
