using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using GameShelf.Shared;

// All interop below is blittable; no runtime marshalling stubs needed (smaller AOT image).
[assembly: System.Runtime.CompilerServices.DisableRuntimeMarshalling]
// System DLLs only, from System32 (see NativeWindow.cs in the widget).
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace GameShelf.Watcher;

/// <summary>
/// Started by the widget right after a game launch. Finds the game's process(es), blocks until they exit,
/// records the session and brings the widget back. Uses only kernel waits once the game is found.
///
/// Args: --game &lt;id&gt; --dir &lt;install folder&gt; [--process &lt;name&gt;] [--timeout &lt;s&gt;] [--return 0|1] [--restart 0|1]
///       [--mode &lt;game mode items, comma-separated&gt; [--close &lt;app exe&gt;]… [--calm &lt;app exe&gt;]… [--toast 0|1]
///        [--smooth &lt;exe name&gt;]… [--throttle &lt;exe name&gt;]… [--limit &lt;Mbit/s&gt;]]
/// Or:   --restore [--quiet] (put back Game mode changes left by a session that didn't end normally; --quiet: the
///       uninstaller, without starting the widget)
/// </summary>
internal static class Program
{
    private const int PollIntervalMs = 2000;
    private const int RestartGraceMs = 3000; // games that restart themselves start the new process within ~1-2 s
    private const int RestartPollMs = 1000;
    private const int MaxWaitHandles = 64; // WaitForMultipleObjects limit
    private const int SaveAttempts = 10, SaveRetryMs = 300;

    private static int Main(string[] args)
    {
        if (args is ["--restore"])
        {
            // From the widget (its start, or "Restore now"), which is running and shows the result.
            if (GameMode.RecoverPending() is not { } recovered) return 3;
            // Nothing was left to put back: no "Back to normal" toast (and sound) for zero changes.
            if (recovered.Total == 0) return 0;
            StartWidget("--restored", recovered.Encode());
            return 0;
        }
        if (args is ["--restore", "--quiet"])
        {
            // From the uninstaller: the same, without bringing the widget back (it's being removed).
            return GameMode.RecoverPending() is null ? 3 : 0;
        }

        string? gameId = null, dir = null, processName = null, modeItems = null;
        int timeoutSeconds = 180;
        bool comeBack = true, toast = true, restartOnCrash = false;
        var closeApps = new List<string>();
        var calmApps = new List<string>();
        var network = new GameMode.NetworkOptions();
        for (int i = 0; i + 1 < args.Length; i += 2)
        {
            switch (args[i])
            {
                case "--game": gameId = args[i + 1]; break;
                case "--dir": dir = args[i + 1]; break;
                case "--process": processName = args[i + 1]; break;
                case "--timeout": int.TryParse(args[i + 1], out timeoutSeconds); break;
                case "--return": comeBack = args[i + 1] != "0"; break;
                case "--restart": restartOnCrash = args[i + 1] == "1"; break;
                case "--mode": modeItems = args[i + 1]; break;
                case "--close": if (closeApps.Count < 32) closeApps.Add(args[i + 1]); break;
                case "--calm": if (calmApps.Count < 32) calmApps.Add(args[i + 1]); break;
                case "--toast": toast = args[i + 1] != "0"; break;
                case "--smooth": if (network.Smooth.Count < 32) network.Smooth.Add(args[i + 1]); break;
                case "--throttle": if (network.Throttle.Count < 32) network.Throttle.Add(args[i + 1]); break;
                case "--limit": if (int.TryParse(args[i + 1], out int limit)) network.UploadLimitMbps = limit; break;
            }
        }
        if (gameId is null || (dir is null && processName is null)) return 2;

        // Game mode: applied now, while the game starts; put back once it's closed (or never showed up).
        var gameMode = modeItems is null ? null : GameMode.Begin(gameId, modeItems, closeApps, network,
            calmApps.FindAll(p => dir is null || !p.StartsWith(Path.TrimEndingDirectorySeparator(dir) + "\\", StringComparison.OrdinalIgnoreCase)));
        // "Game mode is on": only for a widget that is still up (the countdown, or Keep open); one that isn't
        // running doesn't start for it, since it's gone while a game runs.
        if (AppliedArgument(gameMode?.Applied, toast) is { } applied) StartWidget("--applied", applied);
        var matcher = new Matcher(dir, processName) { RaisePriority = gameMode?.RaisePriority == true };

        // Detection phase: the store app may start first, so poll for a while.
        var handles = new List<IntPtr>();
        long deadline = Environment.TickCount64 + timeoutSeconds * 1000L;
        while (true)
        {
            matcher.FindMatches(handles);
            if (handles.Count > 0)
            {
                gameMode?.GameFound(matcher.NamesOf(handles)); // network priority for the game's own exe
                break;
            }
            if (Environment.TickCount64 >= deadline)
            {
                var restored = ToastArgument(gameMode?.End(), toast);
                if (restored is null) StartWidget("--not-detected", gameId);
                else StartWidget("--not-detected", gameId, "--restored", restored);
                return 1;
            }
            Thread.Sleep(PollIntervalMs);
        }

        var start = DateTimeOffset.Now;
        // Saved once: here after the game, or from the system events thread when Windows shuts down or the user
        // signs out while the game runs (the watcher would be ended before it got there).
        int saved = 0;
        TimeSpan Save(DateTimeOffset until, GameExit how, bool reporter) =>
            Interlocked.Exchange(ref saved, 1) == 0 ? SaveSession(gameId, start, until, how, reporter) : TimeSpan.Zero;
        SystemEvents.Listen(() => Save(DateTimeOffset.Now, default, false));

        DateTimeOffset end;
        GameExit exit;
        while (true)
        {
            // Block until every matched process has exited. No polling while the game runs.
            Native.WaitForMultipleObjects((uint)handles.Count, handles.ToArray(), true, Native.INFINITE);
            end = DateTimeOffset.Now;
            exit = matcher.ExitOf(handles);
            CloseAll(handles);

            // Shutting down or signing out: no time to look for a restart, save now.
            if (SystemEvents.Ending) break;
            // Some games restart themselves (launcher → game exe): look again briefly before deciding it's closed.
            // An engine crash reporter showing up now is noted, not mistaken for the game coming back.
            matcher.NoteReporters = true;
            long graceEnd = Environment.TickCount64 + RestartGraceMs;
            while (handles.Count == 0 && Environment.TickCount64 < graceEnd)
            {
                Thread.Sleep(RestartPollMs);
                matcher.FindMatches(handles);
            }
            matcher.NoteReporters = false;
            if (handles.Count == 0) break;
            gameMode?.GameFound(matcher.NamesOf(handles)); // it restarted itself, maybe as another exe
        }

        var played = Save(end, exit, matcher.ReporterSeen);
        // Shutting down or signing out: nothing comes back now. Game mode's journal is put back at the next start
        // (the widget's, or the admin helper's at logon); reopening closed apps during a shutdown would be wrong.
        if (SystemEvents.Ending) return 0;
        bool crashed = restartOnCrash && MayRestart(exit.Code, played);

        if (comeBack)
        {
            // The widget comes back first; Game mode is put back while it starts (the admin part restarts services
            // and can take seconds), and its result follows as a forwarded "--restored" once the widget is up.
            StartProcess(WidgetPath, ReturnArguments(gameId, comeBack, crashed, null)!, 0, out var widget);
            var restored = ToastArgument(gameMode?.End(), toast);
            if (restored is not null)
            {
                // Its window exists once it's idle: the next start is then forwarded to it, never a second widget.
                if (widget != IntPtr.Zero) Native.WaitForInputIdle(widget, WidgetReadyMs);
                StartWidget("--restored", restored);
            }
            if (widget != IntPtr.Zero) Native.CloseHandle(widget);
            return 0;
        }

        // "Come back" off: the widget starts only for a crash restart or the result, then exits again, so it waits
        // for the result instead of being asked twice.
        var result = ToastArgument(gameMode?.End(), toast);
        if (ReturnArguments(gameId, comeBack, crashed, result) is { } widgetArgs) StartWidget(widgetArgs);
        return 0;
    }

    /// <summary>A session at least this long may be restarted after a crash (spec §17).</summary>
    internal static readonly TimeSpan RestartMinSession = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Whether a game with "Restart if it crashes" ended in a way the widget may restart it from (spec §17): a
    /// failure exit code after at least a minute. The widget decides for sure, from the Windows logs and the count.
    /// The rule for the code is <c>CrashAnalysis.IsFailureCode</c>'s (the tests check they agree).
    /// </summary>
    internal static bool MayRestart(uint code, TimeSpan played) =>
        played >= RestartMinSession && (((code & 0x80000000) != 0 && code != 0xC000013A) || code == 0xDEAD);

    /// <summary>
    /// How the widget is brought back after the game: "--returned" with "Come back" on; with it off, still after a
    /// crash that may be restarted ("--crashed": the widget shows only the restart pill), else just Game mode's
    /// toast ("--restored", then it exits). Null: the widget isn't started.
    /// </summary>
    internal static string[]? ReturnArguments(string gameId, bool comeBack, bool crashed, string? restored)
    {
        var args = new List<string>();
        if (comeBack || crashed) args.AddRange(["--returned", gameId]);
        if (!comeBack && crashed) args.Add("--crashed");
        if (restored is not null) args.AddRange(["--restored", restored]);
        return args.Count == 0 ? null : [.. args];
    }

    /// <summary>
    /// The restore result for the widget's "Restored" toast, or null for no toast: nothing was changed, or all
    /// went well and the confirmation is turned off. Something that couldn't be put back is always shown.
    /// </summary>
    internal static string? ToastArgument(RestoreResult? result, bool toast) =>
        result is null || result.Total == 0 || (result.Success && !toast) ? null : result.Encode();

    /// <summary>
    /// What Game mode changed for the widget's "Game mode is on" toast, or null for none: Game mode is off, or all
    /// went well and the confirmation is turned off. Unlike a restore, nothing changed is still worth a word
    /// ("Windows was already set up for gaming"), and something that couldn't be changed is always shown.
    /// </summary>
    internal static string? AppliedArgument(RestoreResult? result, bool toast) =>
        result is null || (result.Success && !toast) ? null : result.Encode();

    private static void CloseAll(List<IntPtr> handles)
    {
        foreach (var h in handles) Native.CloseHandle(h);
        handles.Clear();
    }

    /// <summary>How the game's processes ended: the exit code that tells the most, and that exe's file name.</summary>
    internal readonly record struct GameExit(uint Code, string? Exe);

    /// <summary>
    /// Engine crash reporters and handlers. Never waited on as the game (Unity's runs alongside it the whole
    /// time); one still running after the game exited is recorded as a sign of a crash.
    /// </summary>
    internal static bool IsCrashReporter(ReadOnlySpan<char> fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        foreach (var known in CrashReporters)
            if (name.Equals(known, StringComparison.OrdinalIgnoreCase)) return true;
        return name.StartsWith("CrashSender", StringComparison.OrdinalIgnoreCase); // CrashRpt: CrashSender1403
    }

    private static readonly string[] CrashReporters =
        ["CrashReportClient", "UnityCrashHandler64", "UnityCrashHandler32", "crashpad_handler", "CrashReporter", "BsSndRpt", "BsSndRpt64"];

    /// <summary>
    /// Of several processes' exit codes, the one that tells the most: a failure code (high bit set, as NTSTATUS
    /// errors and HRESULTs have) over another nonzero code over 0.
    /// </summary>
    internal static int MostTelling(IReadOnlyList<uint> codes)
    {
        int best = -1;
        for (int i = 0; i < codes.Count; i++)
        {
            if (best < 0 || Rank(codes[i]) > Rank(codes[best])) best = i;
        }
        return best;

        static int Rank(uint code) => (code & 0x80000000) != 0 ? 2 : code != 0 ? 1 : 0;
    }

    /// <summary>Matches processes by image path inside the install folder, or by explicit process name.</summary>
    private sealed class Matcher(string? dir, string? processName)
    {
        private readonly string? _dirPrefix = string.IsNullOrEmpty(dir) ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir)) + "\\";
        private readonly string? _name = string.IsNullOrEmpty(processName) ? null : Path.GetFileNameWithoutExtension(processName);
        private readonly uint[] _pids = new uint[4096];
        private readonly char[] _path = new char[1024];
        private readonly uint _self = (uint)Environment.ProcessId;
        private readonly Dictionary<IntPtr, string> _names = [];

        private readonly HashSet<uint> _raised = [];

        /// <summary>Game mode: runs each matched game process "Above normal" once, if it's at Normal (never higher).</summary>
        public bool RaisePriority { get; init; }

        /// <summary>While set (after the game exited), a matching crash reporter sets <see cref="ReporterSeen"/>.</summary>
        public bool NoteReporters { get; set; }
        public bool ReporterSeen { get; private set; }

        public void FindMatches(List<IntPtr> handles)
        {
            if (!Native.EnumProcesses(_pids, (uint)(_pids.Length * sizeof(uint)), out var bytes)) return;
            int count = (int)(bytes / sizeof(uint));
            for (int i = 0; i < count && handles.Count < MaxWaitHandles; i++)
            {
                uint pid = _pids[i];
                if (pid == 0 || pid == _self) continue;
                var h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION | Native.SYNCHRONIZE, false, pid);
                if (h == IntPtr.Zero) continue;
                uint len = (uint)_path.Length;
                if (Native.QueryFullProcessImageNameW(h, 0, _path, ref len) && IsMatch(_path.AsSpan(0, (int)len)))
                {
                    var fileName = Path.GetFileName(_path.AsSpan(0, (int)len));
                    if (IsCrashReporter(fileName))
                    {
                        if (NoteReporters) ReporterSeen = true;
                        Native.CloseHandle(h);
                        continue;
                    }
                    handles.Add(h);
                    _names[h] = fileName.ToString();
                    if (RaisePriority && _raised.Add(pid)) Raise(pid, _names[h]);
                }
                else
                {
                    Native.CloseHandle(h);
                }
            }
        }

        private static void Raise(uint pid, string name)
        {
            var h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION | Native.PROCESS_SET_INFORMATION, false, pid);
            if (h == IntPtr.Zero)
            {
                GameMode.Log($"Priority: {name} can't be changed (protected by the game or its anti-cheat)");
                return;
            }
            // A game that already chose its own priority keeps it.
            if (Native.GetPriorityClass(h) == Native.NORMAL_PRIORITY_CLASS &&
                Native.SetPriorityClass(h, Native.ABOVE_NORMAL_PRIORITY_CLASS))
                GameMode.Log($"Priority: {name} runs Above normal");
            Native.CloseHandle(h);
        }

        /// <summary>The exe file names of these matched processes.</summary>
        public List<string> NamesOf(List<IntPtr> handles) =>
            handles.Select(h => _names.GetValueOrDefault(h)).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>Reads the exit codes of the processes that just ended (their handles are still open).</summary>
        public GameExit ExitOf(List<IntPtr> handles)
        {
            var codes = new List<uint>(handles.Count);
            foreach (var h in handles) codes.Add(Native.GetExitCodeProcess(h, out var code) ? code : 0);
            int best = MostTelling(codes);
            var exit = best < 0 ? default : new GameExit(codes[best], _names.GetValueOrDefault(handles[best]));
            foreach (var h in handles) _names.Remove(h);
            return exit;
        }

        private bool IsMatch(ReadOnlySpan<char> path)
        {
            if (_name is not null)
                return Path.GetFileNameWithoutExtension(path).Equals(_name, StringComparison.OrdinalIgnoreCase);
            return path.StartsWith(_dirPrefix, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Saves the session, the crash check's only input, and returns how long the game ran. Time the PC slept
    /// isn't playtime: the session is saved as its awake parts (each an ordinary session; only the last carries
    /// how it ended). Another program (a virus scanner reading the new file, an editor) can hold the sessions file
    /// for a moment, so each line is tried again for a few seconds; one that still can't be saved is written to
    /// error.log instead of being lost without a trace.
    /// </summary>
    private static TimeSpan SaveSession(string gameId, DateTimeOffset start, DateTimeOffset end, GameExit exit, bool crashReporter)
    {
        var parts = SystemEvents.Awake(start, end, SystemEvents.Sleeps(end));
        var played = TimeSpan.Zero;
        for (int i = 0; i < parts.Count; i++)
        {
            bool last = i == parts.Count - 1;
            var (from, to) = parts[i];
            played += to - from;
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    AppendSession(gameId, from, to, last ? exit : default, last && crashReporter);
                    break;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    if (attempt < SaveAttempts)
                    {
                        Thread.Sleep(SaveRetryMs);
                        continue;
                    }
                    LogError($"Couldn't save the session of {gameId} ({from:O} to {to:O}, exit code {exit.Code}): {e.Message}");
                    break;
                }
            }
        }
        return played;
    }

    /// <summary>One line in %LOCALAPPDATA%\GameShelf\error.log, the widget's error log.</summary>
    private static void LogError(string text)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameShelf");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "error.log"), $"{DateTimeOffset.Now:O} [Watcher] {text}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Nowhere left to report it.
        }
    }

    /// <summary>
    /// Appends the session as one line to this year's sessions file (<see cref="Shared.SessionLog"/>), without
    /// reading the file or loading a serializer.
    /// </summary>
    private static void AppendSession(string gameId, DateTimeOffset start, DateTimeOffset end, GameExit exit, bool crashReporter)
    {
        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GameShelf");
        var buffer = new System.Buffers.ArrayBufferWriter<byte>(256);
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("GameId", gameId);
            w.WriteString("Start", start);
            w.WriteString("End", end);
            // How it ended, for the widget's crash check (milestone 11). File names only, no paths.
            w.WriteNumber("ExitCode", exit.Code);
            if (exit.Exe is not null) w.WriteString("Exe", exit.Exe);
            if (crashReporter) w.WriteBoolean("CrashReporter", true);
            w.WriteEndObject();
        }
        Shared.SessionLog.AppendLine(Shared.SessionLog.PathFor(dataDir, end), buffer.WrittenSpan);
    }

    /// <summary>
    /// Quotes one argument by the CommandLineToArgvW rules, so ids with quotes or trailing backslashes
    /// (e.g. from future manual or shortcut entries) can't split into extra arguments.
    /// </summary>
    private static void AppendArgument(StringBuilder sb, string arg)
    {
        sb.Append('"');
        int backslashes = 0;
        foreach (char c in arg)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }
            // Backslashes before a quote are doubled, and the quote itself escaped.
            sb.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes);
            backslashes = 0;
            sb.Append(c);
        }
        sb.Append('\\', backslashes * 2); // before the closing quote
        sb.Append('"');
    }

    private static string WidgetPath => Path.Combine(AppContext.BaseDirectory, "GameShelf.exe");

    /// <summary>How long the result waits for a widget that is still starting (a cold start is about a second).</summary>
    private const uint WidgetReadyMs = 10_000;

    private static void StartWidget(params string[] args) => StartProcess(WidgetPath, args);

    /// <summary>Starts an exe from its own folder; each argument is quoted on its own. False if it couldn't start.</summary>
    internal static bool StartProcess(string exe, string[] args)
    {
        if (!StartProcess(exe, args, 0, out var process)) return false;
        Native.CloseHandle(process);
        return true;
    }

    /// <summary>The same with creation flags, handing back the process handle (the caller closes it).</summary>
    internal static bool StartProcess(string exe, string[] args, uint flags, out IntPtr process)
    {
        process = IntPtr.Zero;
        var sb = new StringBuilder();
        AppendArgument(sb, exe);
        foreach (var arg in args)
        {
            sb.Append(' ');
            AppendArgument(sb, arg);
        }
        var cmd = sb.Append('\0').ToString().ToCharArray();
        var si = new Native.STARTUPINFOW { cb = Marshal.SizeOf<Native.STARTUPINFOW>() };
        if (!Native.CreateProcessW(exe, cmd, IntPtr.Zero, IntPtr.Zero, false, flags, IntPtr.Zero, Path.GetDirectoryName(exe)!, ref si, out var pi))
            return false;
        Native.CloseHandle(pi.hThread);
        process = pi.hProcess;
        return true;
    }
}

internal static partial class Native
{
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const uint SYNCHRONIZE = 0x00100000;
    public const uint INFINITE = 0xFFFFFFFF;

    [LibraryImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumProcesses([Out] uint[] pids, uint cb, out uint bytesReturned);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryFullProcessImageNameW(IntPtr process, uint flags, [Out] char[] name, ref uint size);

    /// <summary>Waits until a started GUI process has made its windows and waits for input (or the timeout).</summary>
    [LibraryImport("user32.dll")]
    public static partial uint WaitForInputIdle(IntPtr process, uint ms);

    [LibraryImport("kernel32.dll")]
    public static partial uint WaitForMultipleObjects(uint count, IntPtr[] handles, [MarshalAs(UnmanagedType.Bool)] bool waitAll, uint ms);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(IntPtr handle);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [StructLayout(LayoutKind.Sequential)]
    public struct STARTUPINFOW
    {
        public int cb;
        public IntPtr lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CreateProcessW(string app, char[] cmdLine, IntPtr procAttr, IntPtr threadAttr,
        [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, IntPtr env, string cwd,
        ref STARTUPINFOW si, out PROCESS_INFORMATION pi);
}
