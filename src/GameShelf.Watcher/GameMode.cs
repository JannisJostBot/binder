using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using GameShelf.Shared;
using Microsoft.Win32;

namespace GameShelf.Watcher;

/// <summary>
/// Game mode's no-admin part (milestone 14, spec §11): Best performance power mode, Game Bar background
/// recording off and closing chosen apps, put back exactly when the game closes. Every change is written to
/// the journal before it's made, so a crash can't lose track of it: the next watcher, the widget's start and
/// "Restore now" put a left-over journal back. Only what is still the way we set it is changed back.
/// (The game's Above normal priority needs no restore; the <see cref="Program"/> matcher sets it.)
/// </summary>
internal sealed class GameMode
{
    private const int CloseWaitMs = 5000;
    private const long MaxJournalBytes = 64 * 1024;
    private const long MaxLogBytes = 256 * 1024;
    private const string GameDvrKey = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";
    private const string HistoricalCapture = "HistoricalCaptureEnabled";

    private readonly Mutex _mutex;
    private readonly Journal _journal;

    /// <summary>The admin part's request for this session (null: not allowed, or no admin item is on).</summary>
    private ElevatedRequest? _elevated;
    private string? _sid;
    private bool _smoothGame;

    /// <summary>What Begin changed and what it couldn't, for the "Game mode is on" toast.</summary>
    public RestoreResult? Applied { get; private set; }

    private int _applied;
    private readonly List<string> _notApplied = [];

    /// <summary>Run the game "Above normal" (never higher).</summary>
    public bool RaisePriority { get; }

    private GameMode(Mutex mutex, Journal journal, bool raisePriority)
    {
        _mutex = mutex;
        _journal = journal;
        RaisePriority = raisePriority;
    }

    /// <summary>The network items' apps, from the widget (exe names; the helper checks them again).</summary>
    internal sealed class NetworkOptions
    {
        /// <summary>Voice chat and the user's own "kept smooth" apps (the game is added once it's found).</summary>
        public List<string> Smooth { get; } = [];
        public List<string> Throttle { get; } = [];
        public int UploadLimitMbps { get; set; } = ElevatedPart.DefaultUploadLimit;
    }

    /// <summary>
    /// Applies the chosen items (comma-separated ids). Null when another game's changes are still applied
    /// (two games started with "Keep open"): the first one's restore stays in charge.
    /// </summary>
    public static GameMode? Begin(string gameId, string items, List<string> closeApps, NetworkOptions? network = null,
        List<string>? calmApps = null)
    {
        // The previous game's watcher may still be putting things back (the widget now returns before it's done,
        // so a quick Play or a crash restart can get here first): wait for it, a kernel wait, at most 30 s.
        var mutex = TryTakeMutex(OtherWatcherWaitMs);
        if (mutex is null)
        {
            Log($"{gameId}: skipped, another game's changes are still applied");
            return null;
        }
        if (Journal.Load() is { } leftOver)
        {
            Log("Putting back changes left from an earlier session");
            WriteLastResult(Restore(leftOver)); // the helper puts back its own left-overs before it applies
        }

        var chosen = new HashSet<string>(items.Split(','), StringComparer.Ordinal);
        var journal = new Journal { GameId = gameId, Started = DateTimeOffset.Now };
        var mode = new GameMode(mutex, journal, chosen.Contains(GameModeInfo.Priority));
        Log($"{gameId}: on ({items}{(closeApps.Count > 0 ? $", closing {closeApps.Count} app(s)" : "")})");
        try
        {
            journal.Save(); // before the first change
            if (chosen.Contains(GameModeInfo.Power)) mode.Count(mode.ApplyPower(), "Power mode");
            if (chosen.Contains(GameModeInfo.GameBar)) mode.Count(mode.ApplyGameBar(), "Game Bar recording");
            if (closeApps.Count > 0) mode.CloseApps(closeApps);
            if (chosen.Contains(GameModeInfo.OneDrive)) mode.Count(mode.PauseOneDrive(), "OneDrive");
            if (calmApps is { Count: > 0 }) mode.CalmApps(calmApps);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Can't keep a journal: change nothing more, and put back what was already changed.
            Log("Couldn't write the journal: " + e.Message);
            mode._notApplied.Add("Game mode");
            mode.Applied = mode.OwnApplied();
            return mode;
        }
        mode.Applied = mode.OwnApplied().Plus(mode.ApplyElevated(chosen, network ?? new NetworkOptions()));
        return mode;
    }

    /// <summary>True: changed. False: couldn't be changed. Null: left as is (it already was that way).</summary>
    private void Count(bool? outcome, string name)
    {
        if (outcome == true) _applied++;
        else if (outcome == false) _notApplied.Add(name);
    }

    private RestoreResult OwnApplied() => new(_applied, _applied + _notApplied.Count, [.. _notApplied]);

    /// <summary>Puts everything back (the game closed, or was never detected).</summary>
    public RestoreResult End()
    {
        try
        {
            var result = Restore(_journal);
            if (_elevated is not null && _sid is not null) result = result.Plus(RestoreElevated(_sid));
            WriteLastResult(result);
            return result;
        }
        finally
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
    }

    // ---- The admin part (the helper, through its scheduled tasks) ----------------------------------------

    /// <summary>
    /// Pauses services, sets network priority and the Windows Update limit, if "Allow…" was done. What the helper
    /// reports it changed, one failure if it didn't answer, or null if it wasn't asked.
    /// </summary>
    private RestoreResult? ApplyElevated(HashSet<string> chosen, NetworkOptions network)
    {
        if (!chosen.Overlaps(GameModeInfo.ElevatedItems)) return null;
        _sid = ElevatedPart.CurrentUserSid();
        if (ElevatedPart.AllowedVersion(_sid) is null)
        {
            Log("Admin part: not allowed yet (Settings › Game mode › Allow…), its items are skipped");
            return null;
        }
        var request = new ElevatedRequest
        {
            Event = ElevatedRequest.NewId(),
            Session = ElevatedRequest.NewId(),
            UploadLimitMbps = Array.IndexOf(ElevatedPart.UploadLimits, network.UploadLimitMbps) >= 0
                ? network.UploadLimitMbps : ElevatedPart.DefaultUploadLimit,
            LimitUpdates = chosen.Contains(GameModeInfo.Updates),
        };
        foreach (var (id, _, _) in ElevatedPart.Services)
            if (chosen.Contains(id)) request.Services.Add(id);
        _smoothGame = chosen.Contains(GameModeInfo.Smooth);
        if (_smoothGame) request.Smooth.AddRange(network.Smooth);
        if (chosen.Contains(GameModeInfo.Uploads)) request.Throttle.AddRange(network.Throttle);
        _elevated = request;
        if (ElevatedClient.Run(ElevatedPart.ApplyTask(_sid!), request, ElevatedClient.ApplyWaitMs))
        {
            Log("Admin part: applied (details in ProgramData\\GameShelf\\elevated.log)");
            return ElevatedPart.ReadResult(_sid!, request.Event) ?? new RestoreResult(0, 0, []);
        }
        Log("Admin part: the helper didn't answer in time");
        return new RestoreResult(0, 1, ["Services and network"]);
    }

    /// <summary>The game's processes were found: mark their exes too ("Game and voice chat first").</summary>
    public void GameFound(List<string> exeNames)
    {
        if (_elevated is not { } request || !_smoothGame || _sid is null) return;
        int before = request.Smooth.Count;
        foreach (var name in exeNames)
            if (ElevatedPart.IsAppName(name) && !request.Smooth.Contains(name, StringComparer.OrdinalIgnoreCase)) request.Smooth.Add(name);
        if (request.Smooth.Count == before) return;
        request.Event = ElevatedRequest.NewId(); // same session: the helper only adds the new ones
        ElevatedClient.Run(ElevatedPart.ApplyTask(_sid), request, ElevatedClient.ApplyWaitMs);
    }

    /// <summary>Asks the helper to put back what it changed; its result, or one failure if it didn't answer.</summary>
    private static RestoreResult? RestoreElevated(string sid)
    {
        if (!File.Exists(ElevatedPart.JournalPath)) return null; // the helper changed nothing (it keeps no empty journal)
        var request = new ElevatedRequest { Event = ElevatedRequest.NewId(), Session = ElevatedRequest.NewId() };
        if (ElevatedClient.Run(ElevatedPart.RestoreTask(sid), request, ElevatedClient.RestoreWaitMs) &&
            ElevatedPart.ReadResult(sid, request.Event) is { } result)
            return result;
        Log("Admin part: the helper didn't report back; see ProgramData\\GameShelf\\elevated.log");
        return new RestoreResult(0, 1, ["Services and network"]);
    }

    /// <summary>
    /// <c>--restore</c>: puts back a journal left by a session that didn't end normally. Null when there is
    /// nothing to do, or a running watcher still owns the changes.
    /// </summary>
    public static RestoreResult? RecoverPending()
    {
        using var mutex = TryTakeMutex();
        if (mutex is null) return null;
        try
        {
            var journal = Journal.Load();
            var sid = File.Exists(ElevatedPart.JournalPath) ? ElevatedPart.CurrentUserSid() : null;
            // Only this account's changes: another account's are left for it (the helper would refuse anyway).
            if (sid is not null && (ElevatedPart.AllowedVersion(sid) is null || !ElevatedPart.JournalIsFor(sid))) sid = null;
            if (journal is null && sid is null) return null;
            Log("Restore of changes left from an earlier session");
            var result = journal is null ? new RestoreResult(0, 0, []) : Restore(journal);
            if (sid is not null) result = result.Plus(RestoreElevated(sid));
            WriteLastResult(result);
            return result;
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private const int OtherWatcherWaitMs = 30_000;

    private static Mutex? TryTakeMutex(int waitMs = 0)
    {
        var mutex = new Mutex(false, GameModeInfo.MutexName);
        try
        {
            if (mutex.WaitOne(waitMs)) return mutex;
        }
        catch (AbandonedMutexException)
        {
            return mutex; // its watcher ended without restoring: ours now, and the journal tells what to put back
        }
        mutex.Dispose();
        return null;
    }

    // ---- Apply ------------------------------------------------------------------------------------------

    private bool? ApplyPower()
    {
        var plan = PowerMode.ActivePlan();
        var mode = plan == PowerMode.BalancedPlan ? PowerMode.ActiveMode() : null;
        if (PowerMode.IsFastest(plan, mode))
        {
            Log("Power: already at best performance, left as is");
            return null;
        }
        if (plan == PowerMode.BalancedPlan && mode is { } before)
        {
            _journal.Power = new PowerChange(PowerChange.Mode, before, PowerMode.BestPerformance);
            _journal.Save();
            if (PowerMode.SetMode(PowerMode.BestPerformance) && PowerMode.ActiveMode() == PowerMode.BestPerformance)
            {
                Log($"Power: mode {before} -> Best performance");
                return true;
            }
            _journal.Power = null;
            _journal.Save();
            Log("Power: couldn't switch the power mode, trying the High performance plan");
        }
        if (plan is { } previous)
        {
            _journal.Power = new PowerChange(PowerChange.Plan, previous, PowerMode.HighPerformancePlan);
            _journal.Save();
            if (PowerMode.SetPlan(PowerMode.HighPerformancePlan))
            {
                Log($"Power: plan {previous} -> High performance");
                return true;
            }
            _journal.Power = null;
            _journal.Save();
        }
        Log("Power: no way to switch on this PC, left as is");
        return false;
    }

    private bool? ApplyGameBar()
    {
        using var key = Registry.CurrentUser.OpenSubKey(GameDvrKey, writable: true);
        if (key?.GetValue(HistoricalCapture) is not int value || value == 0)
        {
            Log("Game Bar: background recording is already off");
            return null;
        }
        _journal.GameBarOff = true;
        _journal.Save();
        key.SetValue(HistoricalCapture, 0, RegistryValueKind.DWord);
        Log("Game Bar: background recording off");
        return true;
    }

    /// <summary>
    /// Asks each app to close the way its close button would (WM_CLOSE to its visible windows) and waits up to
    /// ~5 s. An app that is still running then (it asked to save something, has no window, or runs as admin)
    /// is left open, and won't be reopened.
    /// </summary>
    private void CloseApps(List<string> paths)
    {
        var targets = new List<(string Path, List<IntPtr> Handles)>();
        foreach (var path in paths)
        {
            if (!IsClosableApp(path) || targets.Exists(t => t.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) continue;
            var handles = Processes.Find(path, Native.SYNCHRONIZE);
            if (handles.Count == 0)
            {
                Log($"Apps: {Path.GetFileName(path)} isn't running");
                continue;
            }
            targets.Add((path, handles));
        }
        if (targets.Count == 0) return;

        foreach (var (path, _) in targets) _journal.ClosedApps.Add(path);
        _journal.Save(); // before closing: a crash now must still reopen them

        var all = new List<IntPtr>();
        var windowsPerApp = new int[targets.Count];
        for (int i = 0; i < targets.Count; i++)
        {
            var pids = new HashSet<uint>();
            foreach (var h in targets[i].Handles) pids.Add(Native.GetProcessId(h));
            windowsPerApp[i] = Windows.PostClose(pids);
            if (windowsPerApp[i] > 0) all.AddRange(targets[i].Handles);
        }
        if (all.Count > 0)
            Native.WaitForMultipleObjects((uint)Math.Min(all.Count, 64), all.ToArray(), true, CloseWaitMs);

        for (int i = 0; i < targets.Count; i++)
        {
            var (path, handles) = targets[i];
            bool exited = windowsPerApp[i] > 0 && handles.TrueForAll(h => Native.WaitForSingleObject(h, 0) == Native.WAIT_OBJECT_0);
            if (exited)
            {
                _applied++;
                Log($"Apps: closed {Path.GetFileName(path)}");
            }
            else if (windowsPerApp[i] == 0)
            {
                _journal.ClosedApps.Remove(path); // nothing was asked to close: nothing to reopen
                _notApplied.Add(Path.GetFileNameWithoutExtension(path));
                Log($"Apps: {Path.GetFileName(path)} left open (no window to close)");
            }
            else
            {
                // Asked to close but still running after 5 s (saving, or asking something): it may still close, so it
                // stays in the journal and is reopened after the game if it did (restore leaves a running app alone).
                _notApplied.Add(Path.GetFileNameWithoutExtension(path));
                Log($"Apps: {Path.GetFileName(path)} didn't close within 5 s; reopened after the game if it closes later");
            }
            foreach (var h in handles) Native.CloseHandle(h);
        }
        _journal.Save();
    }

    private const int OneDriveWaitMs = 10_000;

    /// <summary>
    /// Pauses OneDrive's syncing (proposals §20): its own "/shutdown" closes it the way "Quit OneDrive" does, and
    /// "/background" starts it again after the game, only if we closed it. Left running when the Documents folder
    /// is in OneDrive (see <see cref="GameModeInfo.OneDriveHoldsDocuments()"/>).
    /// </summary>
    private bool? PauseOneDrive()
    {
        foreach (var exe in GameModeInfo.OneDriveCandidates())
        {
            var handles = Processes.Find(exe, Native.SYNCHRONIZE);
            if (handles.Count == 0) continue;
            try
            {
                if (GameModeInfo.OneDriveHoldsDocuments())
                {
                    Log("OneDrive: left running, your Documents folder is in OneDrive (games may keep their saves there)");
                    return null;
                }
                _journal.OneDriveExe = exe;
                _journal.Save(); // before closing: a crash now must still start it again
                if (Program.StartProcess(exe, ["/shutdown"]))
                {
                    Native.WaitForMultipleObjects((uint)Math.Min(handles.Count, 64), handles.ToArray(), true, OneDriveWaitMs);
                    if (handles.TrueForAll(h => Native.WaitForSingleObject(h, 0) == Native.WAIT_OBJECT_0))
                    {
                        Log("OneDrive: syncing paused (closed until the game ends)");
                        return true;
                    }
                    // Asked, but still closing: kept in the journal, so it's started again after the game if it
                    // did close (restore leaves a running OneDrive alone).
                    Log("OneDrive: asked to close, still closing after 10 s");
                    return true;
                }
                _journal.OneDriveExe = null;
                _journal.Save();
                Log("OneDrive: didn't close, left running");
                return false;
            }
            finally
            {
                foreach (var h in handles) Native.CloseHandle(h);
            }
        }
        Log("OneDrive: isn't running");
        return null;
    }

    /// <summary>
    /// Puts chosen apps in efficiency mode (EcoQoS, the green leaf in Task Manager; proposals §20) instead of
    /// closing them: Windows runs them at the most efficient speed, on the efficient cores of hybrid CPUs. Only
    /// the execution-speed throttling is set, not Task Manager's idle priority. Each process's previous state is
    /// journaled first, with its id and start time, so only that same process is changed back.
    /// </summary>
    private void CalmApps(List<string> paths)
    {
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!IsClosableApp(path)) continue;
            var name = AppName(path);
            var handles = Processes.Find(path, Native.PROCESS_SET_INFORMATION);
            if (handles.Count == 0)
            {
                Log($"Apps: {Path.GetFileName(path)} isn't running");
                continue;
            }
            int calmed = 0, already = 0;
            bool failed = false;
            foreach (var h in handles)
            {
                try
                {
                    var before = Native.GetThrottling(h);
                    if (Native.IsEfficiencyMode(before))
                    {
                        already++;
                        continue;
                    }
                    var entry = new CalmedApp(path, Native.GetProcessId(h), Native.CreationTime(h), before.ControlMask, before.StateMask);
                    _journal.CalmedApps.Add(entry);
                    _journal.Save();
                    if (Native.SetThrottling(h, before.ControlMask | Native.EXECUTION_SPEED, before.StateMask | Native.EXECUTION_SPEED))
                    {
                        calmed++;
                    }
                    else
                    {
                        _journal.CalmedApps.Remove(entry);
                        _journal.Save();
                        failed = true;
                    }
                }
                finally
                {
                    Native.CloseHandle(h);
                }
            }
            if (calmed > 0)
            {
                _applied++;
                Log($"Apps: {name} in efficiency mode ({calmed} process{(calmed == 1 ? "" : "es")})");
            }
            else if (failed)
            {
                _notApplied.Add(name);
                Log($"Apps: couldn't put {name} in efficiency mode (it may run as admin)");
            }
            else if (already > 0)
            {
                Log($"Apps: {name} is already in efficiency mode, left as is");
            }
        }
    }

    /// <summary>
    /// A full path to an exe that isn't part of Windows, a Store app or Game Shelf (the list comes from
    /// settings.json; the widget checks the same).
    /// </summary>
    internal static bool IsClosableApp(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("..", StringComparison.Ordinal) || path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)) return false;
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrEmpty(windows) && path.StartsWith(Path.TrimEndingDirectorySeparator(windows) + "\\", StringComparison.OrdinalIgnoreCase))
            return false;
        return !path.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Restore ----------------------------------------------------------------------------------------

    /// <summary>In reverse order; only what still is the way we set it; then checks, logs and deletes the journal.</summary>
    private static RestoreResult Restore(Journal journal)
    {
        int total = 0, restored = 0;
        var failed = new List<string>();

        foreach (var path in journal.ClosedApps)
        {
            total++;
            var name = AppName(path);
            var running = Processes.Find(path, 0);
            foreach (var h in running) Native.CloseHandle(h);
            if (running.Count > 0)
            {
                restored++;
                Log($"Apps: {name} is already running again");
            }
            else if (File.Exists(path) && Program.StartProcess(path, []))
            {
                restored++;
                Log($"Apps: reopened {name}");
            }
            else
            {
                failed.Add(name);
                Log($"Apps: couldn't reopen {name}");
            }
        }

        foreach (var group in journal.CalmedApps.GroupBy(a => a.Path, StringComparer.OrdinalIgnoreCase))
        {
            total++;
            var name = AppName(group.Key);
            bool ok = true;
            foreach (var app in group)
            {
                var h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION | Native.PROCESS_SET_INFORMATION, false, app.Pid);
                if (h == IntPtr.Zero) continue; // it ended: nothing to put back
                try
                {
                    // Only the same process (start time), and only if it's still in efficiency mode.
                    if (Native.CreationTime(h) != app.Created || !Native.IsEfficiencyMode(Native.GetThrottling(h))) continue;
                    if (!Native.SetThrottling(h, app.Control, app.State)) ok = false;
                }
                finally
                {
                    Native.CloseHandle(h);
                }
            }
            if (ok)
            {
                restored++;
                Log($"Apps: {name} out of efficiency mode");
            }
            else
            {
                failed.Add(name);
                Log($"Apps: couldn't take {name} out of efficiency mode");
            }
        }

        if (journal.OneDriveExe is { } oneDrive)
        {
            total++;
            var running = Processes.Find(oneDrive, 0);
            foreach (var h in running) Native.CloseHandle(h);
            if (running.Count > 0)
            {
                restored++;
                Log("OneDrive: already running again");
            }
            else if (File.Exists(oneDrive) && Program.StartProcess(oneDrive, ["/background"]))
            {
                restored++;
                Log("OneDrive: started again, syncing resumes");
            }
            else
            {
                failed.Add("OneDrive");
                Log("OneDrive: couldn't start it again");
            }
        }

        if (journal.GameBarOff)
        {
            total++;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(GameDvrKey, writable: true);
                if (key?.GetValue(HistoricalCapture) is int value && value == 0) key.SetValue(HistoricalCapture, 1, RegistryValueKind.DWord);
                if (key?.GetValue(HistoricalCapture) is int now && now != 0) Log("Game Bar: background recording on again");
                else Log("Game Bar: changed meanwhile, left as is");
                restored++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                failed.Add("Game Bar recording");
                Log("Game Bar: couldn't turn background recording back on: " + e.Message);
            }
        }

        if (journal.Power is { } power)
        {
            total++;
            bool isMode = power.Kind == PowerChange.Mode;
            var current = isMode ? PowerMode.ActiveMode() : PowerMode.ActivePlan();
            if (current != power.Set)
            {
                restored++;
                Log("Power: changed meanwhile, left as is");
            }
            else if ((isMode ? PowerMode.SetMode(power.Before) : PowerMode.SetPlan(power.Before)) &&
                     (isMode ? PowerMode.ActiveMode() : PowerMode.ActivePlan()) == power.Before)
            {
                restored++;
                Log($"Power: {(isMode ? "mode" : "plan")} back to {power.Before}");
            }
            else
            {
                failed.Add("Power mode");
                Log("Power: couldn't switch back");
            }
        }

        var result = new RestoreResult(restored, total, failed);
        Log($"Restored {restored} of {total}" + (failed.Count > 0 ? ": " + string.Join(", ", failed) + " not put back" : ""));
        try
        {
            File.Delete(GameModeInfo.JournalPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log("Couldn't clear the journal: " + e.Message);
        }
        return result;
    }

    /// <summary>For "Last restore" in Settings: only when something was changed.</summary>
    private static void WriteLastResult(RestoreResult result)
    {
        if (result.Total == 0) return;
        try
        {
            File.WriteAllLines(GameModeInfo.LastResultPath, [DateTimeOffset.Now.ToString("o"), result.Encode()]);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log("Couldn't save the result: " + e.Message);
        }
    }

    /// <summary>An app's name for the toast: its file name without ".exe".</summary>
    private static string AppName(string path) => Path.GetFileNameWithoutExtension(path);

    // ---- Log --------------------------------------------------------------------------------------------

    /// <summary>One line in gamemode.log ("View log" in Settings), kept under ~256 KB.</summary>
    public static void Log(string text)
    {
        try
        {
            Directory.CreateDirectory(GameModeInfo.Dir);
            var info = new FileInfo(GameModeInfo.LogPath);
            if (info.Exists && info.Length > MaxLogBytes) File.Move(GameModeInfo.LogPath, GameModeInfo.LogPath + ".old", overwrite: true);
            File.AppendAllText(GameModeInfo.LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {text}\r\n");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    // ---- Journal ----------------------------------------------------------------------------------------

    /// <summary>A process put in efficiency mode: which one exactly (id and start time) and its masks before.</summary>
    internal sealed record CalmedApp(string Path, uint Pid, long Created, uint Control, uint State);

    internal sealed record PowerChange(string Kind, Guid Before, Guid Set)
    {
        public const string Mode = "mode", Plan = "plan";
    }

    /// <summary>What was changed and what it was before, flushed to disk before each change.</summary>
    internal sealed class Journal
    {
        public string GameId { get; set; } = "";
        public DateTimeOffset Started { get; set; }
        public PowerChange? Power { get; set; }
        public bool GameBarOff { get; set; }
        public List<string> ClosedApps { get; } = [];
        /// <summary>The OneDrive exe we closed (started again with /background after the game).</summary>
        public string? OneDriveExe { get; set; }
        public List<CalmedApp> CalmedApps { get; } = [];

        public void Save()
        {
            Directory.CreateDirectory(GameModeInfo.Dir);
            var tmp = GameModeInfo.JournalPath + ".tmp";
            using (var file = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                file.Write(ToJson());
                file.Flush(flushToDisk: true);
            }
            File.Move(tmp, GameModeInfo.JournalPath, overwrite: true);
        }

        internal byte[] ToJson()
        {
            var buffer = new System.Buffers.ArrayBufferWriter<byte>(256);
            using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
            {
                w.WriteStartObject();
                w.WriteNumber("Version", 1);
                w.WriteString("GameId", GameId);
                w.WriteString("Started", Started);
                if (Power is { } power)
                {
                    w.WriteStartObject("Power");
                    w.WriteString("Kind", power.Kind);
                    w.WriteString("Before", power.Before);
                    w.WriteString("Set", power.Set);
                    w.WriteEndObject();
                }
                if (GameBarOff) w.WriteBoolean("GameBarOff", true);
                w.WriteStartArray("ClosedApps");
                foreach (var app in ClosedApps) w.WriteStringValue(app);
                w.WriteEndArray();
                if (OneDriveExe is not null) w.WriteString("OneDriveExe", OneDriveExe);
                if (CalmedApps.Count > 0)
                {
                    w.WriteStartArray("CalmedApps");
                    foreach (var app in CalmedApps)
                    {
                        w.WriteStartObject();
                        w.WriteString("Path", app.Path);
                        w.WriteNumber("Pid", app.Pid);
                        w.WriteNumber("Created", app.Created);
                        w.WriteNumber("Control", app.Control);
                        w.WriteNumber("State", app.State);
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                }
                w.WriteEndObject();
            }
            return buffer.WrittenSpan.ToArray();
        }

        /// <summary>The journal on disk, or null. An unreadable one is kept aside (.bad) and logged, never acted on.</summary>
        public static Journal? Load()
        {
            var path = GameModeInfo.JournalPath;
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) return null;
                if (info.Length > MaxJournalBytes) throw new FormatException("too large");
                return Parse(File.ReadAllBytes(path));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log("Couldn't read the journal: " + e.Message);
                return null;
            }
            catch (Exception e) when (e is FormatException or JsonException or InvalidOperationException or KeyNotFoundException)
            {
                Log("The journal is damaged, kept as gamemode-journal.json.bad: " + e.Message);
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
                GameId = root.GetProperty("GameId").GetString() ?? "",
                Started = root.GetProperty("Started").GetDateTimeOffset(),
            };
            if (root.TryGetProperty("Power", out var power))
            {
                var kind = power.GetProperty("Kind").GetString();
                if (kind is not (PowerChange.Mode or PowerChange.Plan)) throw new FormatException("unknown power change");
                journal.Power = new PowerChange(kind, power.GetProperty("Before").GetGuid(), power.GetProperty("Set").GetGuid());
            }
            journal.GameBarOff = root.TryGetProperty("GameBarOff", out var gameBar) && gameBar.GetBoolean();
            if (root.TryGetProperty("ClosedApps", out var apps))
            {
                foreach (var app in apps.EnumerateArray())
                {
                    // Reopened only if it's still a plain app path (see IsClosableApp), and at most 32 of them.
                    if (app.GetString() is { } p && IsClosableApp(p) && journal.ClosedApps.Count < 32) journal.ClosedApps.Add(p);
                }
            }
            // Started again only if it's one of OneDrive's own install places.
            if (root.TryGetProperty("OneDriveExe", out var oneDrive) && oneDrive.GetString() is { } exe &&
                GameModeInfo.OneDriveCandidates().Contains(exe, StringComparer.OrdinalIgnoreCase))
                journal.OneDriveExe = exe;
            if (root.TryGetProperty("CalmedApps", out var calmed))
            {
                foreach (var app in calmed.EnumerateArray())
                {
                    if (journal.CalmedApps.Count >= 64) break;
                    var path = app.GetProperty("Path").GetString();
                    if (path is null || !IsClosableApp(path)) continue;
                    journal.CalmedApps.Add(new CalmedApp(path, app.GetProperty("Pid").GetUInt32(), app.GetProperty("Created").GetInt64(),
                        app.GetProperty("Control").GetUInt32() & Native.THROTTLE_VALID, app.GetProperty("State").GetUInt32() & Native.THROTTLE_VALID));
                }
            }
            return journal;
        }
    }

    // ---- Processes and windows --------------------------------------------------------------------------

    private static class Processes
    {
        private static readonly uint[] Pids = new uint[4096];
        private static readonly char[] Buffer = new char[1024];

        /// <summary>Handles to every process running from exactly this exe (caller closes them).</summary>
        public static List<IntPtr> Find(string exe, uint extraAccess)
        {
            var found = new List<IntPtr>();
            if (!Native.EnumProcesses(Pids, (uint)(Pids.Length * sizeof(uint)), out var bytes)) return found;
            int count = (int)(bytes / sizeof(uint));
            uint self = (uint)Environment.ProcessId;
            for (int i = 0; i < count && found.Count < 64; i++)
            {
                if (Pids[i] == 0 || Pids[i] == self) continue;
                var h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION | extraAccess, false, Pids[i]);
                if (h == IntPtr.Zero) continue;
                uint len = (uint)Buffer.Length;
                if (Native.QueryFullProcessImageNameW(h, 0, Buffer, ref len) &&
                    Buffer.AsSpan(0, (int)len).Equals(exe, StringComparison.OrdinalIgnoreCase))
                    found.Add(h);
                else
                    Native.CloseHandle(h);
            }
            return found;
        }
    }

    private static unsafe class Windows
    {
        private static HashSet<uint>? s_pids;
        private static List<IntPtr>? s_found;

        /// <summary>Posts WM_CLOSE to the visible top-level windows of these processes. Returns how many.</summary>
        public static int PostClose(HashSet<uint> pids)
        {
            s_pids = pids;
            s_found = [];
            Native.EnumWindows(&OnWindow, IntPtr.Zero);
            foreach (var hwnd in s_found) Native.PostMessageW(hwnd, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            int count = s_found.Count;
            s_pids = null;
            s_found = null;
            return count;
        }

        [UnmanagedCallersOnly]
        private static int OnWindow(IntPtr hwnd, IntPtr lParam)
        {
            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            if (s_pids!.Contains(pid) && Native.IsWindowVisible(hwnd) && Native.GetWindow(hwnd, Native.GW_OWNER) == IntPtr.Zero)
                s_found!.Add(hwnd);
            return 1; // keep enumerating
        }
    }
}

internal static unsafe partial class Native
{
    public const uint PROCESS_SET_INFORMATION = 0x0200;
    public const uint NORMAL_PRIORITY_CLASS = 0x20;
    public const uint ABOVE_NORMAL_PRIORITY_CLASS = 0x8000;
    public const uint WAIT_OBJECT_0 = 0;
    public const uint WM_CLOSE = 0x0010;
    public const uint GW_OWNER = 4;

    // Efficiency mode (EcoQoS): SetProcessInformation(ProcessPowerThrottling) with EXECUTION_SPEED.
    private const int ProcessPowerThrottling = 4;
    public const uint EXECUTION_SPEED = 0x1;
    public const uint THROTTLE_VALID = 0x7; // EXECUTION_SPEED | 0x2 | IGNORE_TIMER_RESOLUTION

    [StructLayout(LayoutKind.Sequential)]
    public struct PowerThrottling
    {
        public uint Version, ControlMask, StateMask;
    }

    public static PowerThrottling GetThrottling(IntPtr process)
    {
        var state = new PowerThrottling { Version = 1 };
        // Older Windows doesn't answer this query; "nothing set" is then the truth for our purpose.
        if (!GetProcessInformation(process, ProcessPowerThrottling, &state, (uint)sizeof(PowerThrottling)))
            state = new PowerThrottling { Version = 1 };
        return state;
    }

    public static bool IsEfficiencyMode(PowerThrottling state) =>
        (state.ControlMask & EXECUTION_SPEED) != 0 && (state.StateMask & EXECUTION_SPEED) != 0;

    public static bool SetThrottling(IntPtr process, uint control, uint state)
    {
        var value = new PowerThrottling { Version = 1, ControlMask = control & THROTTLE_VALID, StateMask = state & control & THROTTLE_VALID };
        return SetProcessInformation(process, ProcessPowerThrottling, &value, (uint)sizeof(PowerThrottling));
    }

    /// <summary>The process's start time (FILETIME ticks), so a reused process id isn't mistaken for it.</summary>
    public static long CreationTime(IntPtr process) =>
        GetProcessTimes(process, out var created, out _, out _, out _) ? created : 0;

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessInformation(IntPtr process, int infoClass, void* info, uint size);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessInformation(IntPtr process, int infoClass, void* info, uint size);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);

    [LibraryImport("kernel32.dll")]
    public static partial uint GetPriorityClass(IntPtr process);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetPriorityClass(IntPtr process, uint priorityClass);

    [LibraryImport("kernel32.dll")]
    public static partial uint GetProcessId(IntPtr process);

    [LibraryImport("kernel32.dll")]
    public static partial uint WaitForSingleObject(IntPtr handle, uint ms);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumWindows(delegate* unmanaged<IntPtr, IntPtr, int> callback, IntPtr lParam);

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetWindow(IntPtr hwnd, uint command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}
