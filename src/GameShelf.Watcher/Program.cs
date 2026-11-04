using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

// All interop below is blittable; no runtime marshalling stubs needed (smaller AOT image).
[assembly: System.Runtime.CompilerServices.DisableRuntimeMarshalling]
// System DLLs only, from System32 (see NativeWindow.cs in the widget).
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace GameShelf.Watcher;

/// <summary>
/// Started by the widget right after a game launch. Finds the game's process(es), blocks until they exit,
/// records the session and brings the widget back. Uses only kernel waits once the game is found.
///
/// Args: --game &lt;id&gt; --dir &lt;install folder&gt; [--process &lt;name&gt;] [--timeout &lt;s&gt;] [--return 0|1]
/// </summary>
internal static class Program
{
    private const int PollIntervalMs = 2000;
    private const int RestartGraceMs = 5000;
    private const int RestartPollMs = 1000;
    private const int MaxWaitHandles = 64; // WaitForMultipleObjects limit

    private static int Main(string[] args)
    {
        string? gameId = null, dir = null, processName = null;
        int timeoutSeconds = 180;
        bool comeBack = true;
        for (int i = 0; i + 1 < args.Length; i += 2)
        {
            switch (args[i])
            {
                case "--game": gameId = args[i + 1]; break;
                case "--dir": dir = args[i + 1]; break;
                case "--process": processName = args[i + 1]; break;
                case "--timeout": int.TryParse(args[i + 1], out timeoutSeconds); break;
                case "--return": comeBack = args[i + 1] != "0"; break;
            }
        }
        if (gameId is null || (dir is null && processName is null)) return 2;

        var matcher = new Matcher(dir, processName);

        // Detection phase: the store app may start first, so poll for a while.
        var handles = new List<IntPtr>();
        long deadline = Environment.TickCount64 + timeoutSeconds * 1000L;
        while (true)
        {
            matcher.FindMatches(handles);
            if (handles.Count > 0) break;
            if (Environment.TickCount64 >= deadline)
            {
                StartWidget("--not-detected", gameId);
                return 1;
            }
            Thread.Sleep(PollIntervalMs);
        }

        var start = DateTimeOffset.Now;
        DateTimeOffset end;
        GameExit exit;
        while (true)
        {
            // Block until every matched process has exited. No polling while the game runs.
            Native.WaitForMultipleObjects((uint)handles.Count, handles.ToArray(), true, Native.INFINITE);
            end = DateTimeOffset.Now;
            exit = matcher.ExitOf(handles);
            CloseAll(handles);

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
        }

        try { AppendSession(gameId, start, end, exit, matcher.ReporterSeen); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

        if (comeBack) StartWidget("--returned", gameId);
        return 0;
    }

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
                }
                else
                {
                    Native.CloseHandle(h);
                }
            }
        }

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

    /// <summary>Appends one entry to the sessions.json array without loading a serializer.</summary>
    private static void AppendSession(string gameId, DateTimeOffset start, DateTimeOffset end, GameExit exit, bool crashReporter)
    {
        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GameShelf");
        var path = Path.Combine(dataDir, "sessions.json");
        Directory.CreateDirectory(dataDir);

        var buffer = new System.Buffers.ArrayBufferWriter<byte>(256);
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
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
        var entry = Encoding.UTF8.GetString(buffer.WrittenSpan).Replace("\n", "\n  ");

        string existing = File.Exists(path) ? File.ReadAllText(path) : "";
        int close = existing.LastIndexOf(']');
        int open = existing.IndexOf('[');
        string json;
        if (open < 0 || close < open)
            json = "[\n  " + entry + "\n]";
        else
        {
            bool empty = existing.AsSpan(open + 1, close - open - 1).Trim().IsEmpty;
            json = existing[..close].TrimEnd() + (empty ? "\n  " : ",\n  ") + entry + "\n]";
        }

        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
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

    private static void StartWidget(string flag, string gameId)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "GameShelf.exe");
        var sb = new StringBuilder();
        AppendArgument(sb, exe);
        sb.Append(' ').Append(flag).Append(' ');
        AppendArgument(sb, gameId);
        var cmd = sb.Append('\0').ToString().ToCharArray();
        var si = new Native.STARTUPINFOW { cb = Marshal.SizeOf<Native.STARTUPINFOW>() };
        if (Native.CreateProcessW(exe, cmd, IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, AppContext.BaseDirectory, ref si, out var pi))
        {
            Native.CloseHandle(pi.hProcess);
            Native.CloseHandle(pi.hThread);
        }
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
