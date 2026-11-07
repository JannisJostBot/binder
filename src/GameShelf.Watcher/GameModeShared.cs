using System.Runtime.InteropServices;

namespace GameShelf.Shared;

/// <summary>
/// Game mode (milestone 14, spec §11): the item ids, the files and the restore result. Compiled into the
/// watcher (which applies and restores), the widget (Settings, the "Restored" toast, recovery) and the elevated
/// helper (<see cref="ElevatedPart"/>).
/// </summary>
public static class GameModeInfo
{
    public const string Power = "power";
    public const string Priority = "priority";
    public const string GameBar = "gamebar";
    // Need the elevated helper (admin rights, once): see ElevatedPart.
    public const string WindowsSearch = "wsearch";
    public const string SysMain = "sysmain";
    public const string Telemetry = "diagtrack";
    public const string PrintSpooler = "spooler";
    public const string Smooth = "smooth";      // game and voice chat first (DSCP 46)
    public const string Uploads = "uploads";    // slow down background uploads
    public const string Updates = "updates";    // limit Windows Update downloads (Delivery Optimization)

    /// <summary>The items the user can turn on, in the order of the Settings page.</summary>
    public static readonly string[] Items =
        [Power, Priority, WindowsSearch, SysMain, Telemetry, PrintSpooler, GameBar, Smooth, Uploads, Updates];

    /// <summary>On by default (spec §11 table): all but Print Spooler and Game Bar recording.</summary>
    public static readonly string[] DefaultItems =
        [Power, Priority, WindowsSearch, SysMain, Telemetry, Smooth, Uploads, Updates];

    /// <summary>The items only the elevated helper can apply.</summary>
    public static readonly string[] ElevatedItems = [WindowsSearch, SysMain, Telemetry, PrintSpooler, Smooth, Uploads, Updates];

    public static bool IsElevatedItem(string id) => Array.IndexOf(ElevatedItems, id) >= 0;

    /// <summary>Held by the watcher from the first change until everything is put back.</summary>
    public const string MutexName = @"Local\GameShelf.GameMode";

    public static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameShelf");

    /// <summary>The write-ahead journal: what was changed and what it was before. Exists only while changes are applied.</summary>
    public static string JournalPath => Path.Combine(Dir, "gamemode-journal.json");

    public static string LogPath => Path.Combine(Dir, "gamemode.log");

    /// <summary>The last restore: its time on the first line, <see cref="RestoreResult.Encode"/> on the second.</summary>
    public static string LastResultPath => Path.Combine(Dir, "gamemode-last.txt");

    /// <summary>What the watcher asks the elevated helper to do (<see cref="ElevatedRequest"/>); untrusted to the helper.</summary>
    public static string RequestPath => Path.Combine(Dir, "gamemode-request.txt");

    public static bool IsKnownItem(string id) => Array.IndexOf(Items, id) >= 0;

    /// <summary>The last restore, or null if there was none (or the file is unreadable).</summary>
    public static (DateTimeOffset When, RestoreResult Result)? LastResult()
    {
        try
        {
            var info = new FileInfo(LastResultPath);
            if (!info.Exists || info.Length > 4096) return null;
            var lines = File.ReadAllLines(LastResultPath);
            if (lines.Length < 2 || !DateTimeOffset.TryParse(lines[0], System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var when)) return null;
            return RestoreResult.Parse(lines[1]) is { } result ? (when, result) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>
/// How a restore went: how many changes were put back of how many, and the names of the ones that weren't.
/// Passed to the widget as <c>--restored &lt;encoded&gt;</c>, e.g. "5/5" or "4/5|Spotify".
/// </summary>
public sealed record RestoreResult(int Restored, int Total, List<string> Failed)
{
    public bool Success => Failed.Count == 0;

    public string Encode()
    {
        var text = Restored + "/" + Total;
        foreach (var name in Failed) text += "|" + name.Replace("|", "");
        return text;
    }

    public static RestoreResult? Parse(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 2048) return null;
        var parts = text.Split('|');
        var counts = parts[0].Split('/');
        if (counts.Length != 2 || !int.TryParse(counts[0], out int restored) || !int.TryParse(counts[1], out int total) ||
            restored < 0 || total < 0 || restored > total) return null;
        var failed = new List<string>();
        for (int i = 1; i < parts.Length && failed.Count < 20; i++)
            if (parts[i].Trim() is { Length: > 0 } name) failed.Add(name.Length > 80 ? name[..80] : name);
        return new RestoreResult(restored, total, failed);
    }

    /// <summary>Both parts together: the watcher's own and the elevated helper's.</summary>
    public RestoreResult Plus(RestoreResult? other) =>
        other is null ? this : new RestoreResult(Restored + other.Restored, Total + other.Total, [.. Failed, .. other.Failed]);

    /// <summary>"Restored successfully", or "Restored 4 of 5 · Spotify couldn't be put back".</summary>
    public string Summary()
    {
        if (Success) return "Restored successfully";
        var names = Failed.Count == 1 ? Failed[0] : Failed[0] + $" and {Failed.Count - 1} more";
        return $"Restored {Restored} of {Total} · {names} couldn't be put back";
    }
}

/// <summary>
/// The power plan and the power mode (the Settings › Power mode choice, an "overlay" on the Balanced plan). The
/// overlay functions are undocumented (approved, spec §11); all of this works without admin rights.
/// </summary>
public static unsafe partial class PowerMode
{
    public static readonly Guid BalancedPlan = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid HighPerformancePlan = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid UltimatePlan = new("e9a42b02-d5df-448d-aa00-03f14749eb61");

    /// <summary>Power mode "Best performance". Balanced is <see cref="Guid.Empty"/>.</summary>
    public static readonly Guid BestPerformance = new("ded574b5-45a0-4f42-8737-46345c09c238");
    public static readonly Guid BestEfficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a");

    public static Guid? ActivePlan()
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out var ptr) != 0 || ptr == IntPtr.Zero) return null;
        try
        {
            return *(Guid*)ptr;
        }
        finally
        {
            LocalFree(ptr);
        }
    }

    public static Guid? ActiveMode() => PowerGetEffectiveOverlayScheme(out var mode) == 0 ? mode : null;

    public static bool SetPlan(Guid plan) => PowerSetActiveScheme(IntPtr.Zero, in plan) == 0;

    public static bool SetMode(Guid mode) => PowerSetActiveOverlayScheme(mode) == 0;

    /// <summary>Already as fast as it gets: Best performance mode, or a High / Ultimate performance plan.</summary>
    public static bool IsFastest(Guid? plan, Guid? mode) =>
        plan == HighPerformancePlan || plan == UltimatePlan || (plan == BalancedPlan && mode == BestPerformance);

    /// <summary>"Balanced", "Best performance", … as Settings › Power names it now, for the Game mode page.</summary>
    public static string Describe()
    {
        var plan = ActivePlan();
        if (plan == HighPerformancePlan) return "High performance plan";
        if (plan == UltimatePlan) return "Ultimate performance plan";
        if (plan != BalancedPlan) return "a custom power plan";
        var mode = ActiveMode();
        if (mode == BestPerformance) return "Best performance";
        if (mode == BestEfficiency) return "Best power efficiency";
        return "Balanced";
    }

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerGetActiveScheme(IntPtr rootKey, out IntPtr activePolicy);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerSetActiveScheme(IntPtr rootKey, in Guid scheme);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerGetEffectiveOverlayScheme(out Guid overlay);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerSetActiveOverlayScheme(Guid overlay);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr LocalFree(IntPtr mem);
}

/// <summary>
/// Game mode's admin part (spec §11, "Elevation and security"): GameShelf.Elevated.exe in Program Files, run
/// through two scheduled tasks with highest privileges that the user's one "Allow…" registered. The watcher runs a
/// task and waits on a named event; the helper reads <see cref="GameModeInfo.RequestPath"/> (untrusted: it acts only
/// on the ids and names below) and keeps its journal in an admin-only folder in ProgramData.
/// </summary>
public static partial class ElevatedPart
{
    public const string ExeName = "GameShelf.Elevated.exe";

    /// <summary>%ProgramFiles%\GameShelf: only admins can write there, so the task can't be pointed at anything else.</summary>
    public static string InstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GameShelf");

    public static string InstalledExe => Path.Combine(InstallDir, ExeName);

    /// <summary>%ProgramData%\GameShelf: written by the helper only (admins and SYSTEM), readable by users.</summary>
    public static string DataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GameShelf");

    public static string JournalPath => Path.Combine(DataDir, "journal.json");
    public static string LogPath => Path.Combine(DataDir, "elevated.log");

    /// <summary>Written by "Allow…" for this user (the helper's version inside); the watcher and Settings check it.</summary>
    public static string MarkerPath(string sid) => Path.Combine(DataDir, $"allowed-{sid}.txt");

    /// <summary>The last result for this user: the request's event id on line 1, the result on line 2.</summary>
    public static string ResultPath(string sid) => Path.Combine(DataDir, $"result-{sid}.txt");

    public static string ApplyTask(string sid) => @"\GameShelf Apply " + sid;
    public static string RestoreTask(string sid) => @"\GameShelf Restore " + sid;

    public const string EventPrefix = @"Local\GameShelf.Elevated.";

    /// <summary>This build's version (Directory.Build.props), which "Allow…" writes into the marker.</summary>
    public static string BuildVersion
    {
        get
        {
            var v = typeof(ElevatedPart).Assembly.GetName().Version;
            return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";
        }
    }

    /// <summary>Upload limits offered (Mbit/s); the default is 5.</summary>
    public static readonly int[] UploadLimits = [1, 5, 20];
    public const int DefaultUploadLimit = 5;

    /// <summary>Windows Update (Delivery Optimization) background downloads during a game: this share of the bandwidth.</summary>
    public const int UpdatePercent = 10;

    /// <summary>The services Game mode may pause: item id, service name, and the name the toast uses.</summary>
    public static readonly (string Id, string Service, string Name)[] Services =
    [
        (GameModeInfo.WindowsSearch, "WSearch", "Windows Search"),
        (GameModeInfo.SysMain, "SysMain", "SysMain"),
        (GameModeInfo.Telemetry, "DiagTrack", "Telemetry"),
        (GameModeInfo.PrintSpooler, "Spooler", "Print Spooler"),
    ];

    public static (string Id, string Service, string Name)? ServiceFor(string id)
    {
        foreach (var s in Services)
            if (s.Id == id) return s;
        return null;
    }

    /// <summary>Upload apps slowed down by default (spec §11: OneDrive, Google Drive, Dropbox; editable).</summary>
    public static readonly string[] DefaultThrottledApps = ["OneDrive.exe", "GoogleDriveFS.exe", "Dropbox.exe"];

    /// <summary>Voice chat kept smooth when installed: the name shown, its exe names, and where it installs.</summary>
    public static readonly (string Name, string[] Exes, string[] Places)[] VoiceApps =
    [
        ("Discord", ["Discord.exe"], [@"%LOCALAPPDATA%\Discord"]),
        ("TeamSpeak", ["TeamSpeak.exe", "ts3client_win64.exe"],
            [@"%LOCALAPPDATA%\Programs\TeamSpeak", @"%ProgramFiles%\TeamSpeak", @"%ProgramFiles%\TeamSpeak 3 Client", @"%LOCALAPPDATA%\TeamSpeak 3 Client"]),
        ("Mumble", ["mumble.exe"], [@"%ProgramFiles%\Mumble"]),
        ("Teams", ["ms-teams.exe", "Teams.exe"], [@"%LOCALAPPDATA%\Microsoft\Teams", @"%LOCALAPPDATA%\Microsoft\WindowsApps\ms-teams.exe"]),
        ("Zoom", ["Zoom.exe"], [@"%APPDATA%\Zoom\bin", @"%ProgramFiles%\Zoom\bin"]),
    ];

    /// <summary>The voice apps installed on this PC (a few folder checks, when asked).</summary>
    public static List<(string Name, string[] Exes)> InstalledVoiceApps()
    {
        var found = new List<(string, string[])>();
        foreach (var (name, exes, places) in VoiceApps)
        {
            foreach (var place in places)
            {
                var path = Environment.ExpandEnvironmentVariables(place);
                if (Directory.Exists(path) || File.Exists(path))
                {
                    found.Add((name, exes));
                    break;
                }
            }
        }
        return found;
    }

    /// <summary>
    /// An exe file name (no folder) a QoS policy may match: letters, digits and a few safe signs only, so it can
    /// go into a policy name and a WQL string as is.
    /// </summary>
    public static bool IsAppName(string? name)
    {
        if (name is null || name.Length is < 5 or > 64 || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
        foreach (char c in name)
            if (!char.IsLetterOrDigit(c) && " ._()+-[]&!#".IndexOf(c) < 0) return false;
        return name[0] != '.' && name[0] != ' ';
    }

    /// <summary>A user account's SID as text ("S-1-5-21-…"): what tasks, markers and results are named by.</summary>
    public static bool IsUserSid(string? sid)
    {
        if (sid is null || sid.Length > 184 || !sid.StartsWith("S-1-5-21-", StringComparison.Ordinal)) return false;
        foreach (char c in sid.AsSpan(9))
            if (c != '-' && !char.IsAsciiDigit(c)) return false;
        return !sid.EndsWith('-') && !sid.Contains("--", StringComparison.Ordinal);
    }

    /// <summary>The SID of the user this process runs as, or null.</summary>
    public static unsafe string? CurrentUserSid()
    {
        const int TokenUser = 1;
        var buffer = stackalloc byte[256];
        // GetCurrentProcessToken() is the pseudo handle -4: nothing to close.
        if (!GetTokenInformation(-4, TokenUser, buffer, 256, out _)) return null;
        if (!ConvertSidToStringSidW(*(IntPtr*)buffer, out var text) || text == IntPtr.Zero) return null;
        try
        {
            var sid = new string((char*)text);
            return IsUserSid(sid) ? sid : null;
        }
        finally
        {
            LocalFree(text);
        }
    }

    /// <summary>"Allow…" was done for this user and the helper is in place: the helper's version, else null.</summary>
    public static string? AllowedVersion(string? sid)
    {
        if (sid is null) return null;
        try
        {
            var marker = new FileInfo(MarkerPath(sid));
            if (!marker.Exists || marker.Length > 64 || !File.Exists(InstalledExe)) return null;
            return File.ReadAllText(marker.FullName).Trim();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The helper's last result for this request (null if it's from another request or unreadable).</summary>
    public static RestoreResult? ReadResult(string sid, string eventId)
    {
        try
        {
            var info = new FileInfo(ResultPath(sid));
            if (!info.Exists || info.Length > 4096) return null;
            var lines = File.ReadAllLines(info.FullName);
            return lines.Length >= 2 && lines[0] == eventId ? RestoreResult.Parse(lines[1]) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [LibraryImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetTokenInformation(nint token, int infoClass, byte* info, int length, out int returned);

    [LibraryImport("advapi32.dll", EntryPoint = "ConvertSidToStringSidW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ConvertSidToStringSidW(IntPtr sid, out IntPtr text);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr LocalFree(IntPtr mem);
}

/// <summary>
/// One request from the watcher to the helper, as "key=value" lines. Checked on both ends; the helper treats
/// the file as untrusted and drops whatever doesn't fit.
/// </summary>
public sealed class ElevatedRequest
{
    public const int MaxBytes = 8 * 1024;
    private const int MaxApps = 32;

    /// <summary>32 hex digits: names the event the helper sets when it's done, and is echoed in its result.</summary>
    public string Event { get; set; } = "";
    /// <summary>32 hex digits, one per game session: a second Apply in the same session only adds.</summary>
    public string Session { get; set; } = "";
    /// <summary>Item ids of the services to pause (only ids in <see cref="ElevatedPart.Services"/>).</summary>
    public List<string> Services { get; } = [];
    /// <summary>Exe names marked DSCP 46: the game and voice chat (empty: the item is off).</summary>
    public List<string> Smooth { get; } = [];
    /// <summary>Exe names whose uploads are throttled (empty: the item is off).</summary>
    public List<string> Throttle { get; } = [];
    public int UploadLimitMbps { get; set; } = ElevatedPart.DefaultUploadLimit;
    public bool LimitUpdates { get; set; }

    public static string NewId() => Guid.NewGuid().ToString("N");

    public static bool IsId(string? id)
    {
        if (id is not { Length: 32 }) return false;
        foreach (char c in id)
            if (!char.IsAsciiHexDigitLower(c) && !char.IsAsciiDigit(c)) return false;
        return true;
    }

    public string Encode()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("event=").Append(Event).Append('\n');
        sb.Append("session=").Append(Session).Append('\n');
        sb.Append("services=").Append(string.Join(',', Services.Where(id => ElevatedPart.ServiceFor(id) is not null))).Append('\n');
        sb.Append("smooth=").Append(string.Join(',', Smooth.Where(ElevatedPart.IsAppName).Take(MaxApps))).Append('\n');
        sb.Append("throttle=").Append(string.Join(',', Throttle.Where(ElevatedPart.IsAppName).Take(MaxApps))).Append('\n');
        sb.Append("limit=").Append(UploadLimitMbps).Append('\n');
        sb.Append("updates=").Append(LimitUpdates ? '1' : '0').Append('\n');
        return sb.ToString();
    }

    /// <summary>The request, or null if it's malformed. Unknown ids and names that aren't plain exe names are dropped.</summary>
    public static ElevatedRequest? Parse(string? text)
    {
        if (text is null || text.Length > MaxBytes) return null;
        var request = new ElevatedRequest();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var value = line[(eq + 1)..];
            switch (line[..eq])
            {
                case "event": request.Event = value; break;
                case "session": request.Session = value; break;
                case "services":
                    foreach (var id in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                        if (ElevatedPart.ServiceFor(id) is not null && !request.Services.Contains(id)) request.Services.Add(id);
                    break;
                case "smooth": AddApps(request.Smooth, value); break;
                case "throttle": AddApps(request.Throttle, value); break;
                case "limit":
                    if (!int.TryParse(value, out int limit) || Array.IndexOf(ElevatedPart.UploadLimits, limit) < 0) return null;
                    request.UploadLimitMbps = limit;
                    break;
                case "updates": request.LimitUpdates = value == "1"; break;
            }
        }
        if (!IsId(request.Event) || !IsId(request.Session)) return null;
        // An app kept smooth is never slowed down (spec §11).
        request.Throttle.RemoveAll(t => request.Smooth.Contains(t, StringComparer.OrdinalIgnoreCase));
        return request;
    }

    private static void AddApps(List<string> list, string value)
    {
        foreach (var name in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (list.Count >= MaxApps) break;
            if (ElevatedPart.IsAppName(name) && !list.Contains(name, StringComparer.OrdinalIgnoreCase)) list.Add(name);
        }
    }
}

/// <summary>A service's state, read with query rights only (no admin needed): for Settings and the helper.</summary>
public static unsafe partial class ServiceState
{
    public enum Kind { Missing, Running, Stopped, Disabled }

    public const uint SC_MANAGER_CONNECT = 0x1, SERVICE_QUERY_CONFIG = 0x1, SERVICE_QUERY_STATUS = 0x4;
    public const uint SERVICE_STOPPED = 1, SERVICE_RUNNING = 4;
    public const int SERVICE_DISABLED = 4;

    public static Kind Of(string service)
    {
        var scm = OpenSCManagerW(null, null, SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero) return Kind.Missing;
        try
        {
            var h = OpenServiceW(scm, service, SERVICE_QUERY_STATUS | SERVICE_QUERY_CONFIG);
            if (h == IntPtr.Zero) return Kind.Missing;
            try
            {
                var state = CurrentState(h);
                if (state == 0) return Kind.Missing;
                if (state != SERVICE_STOPPED) return Kind.Running;
                return StartType(h) == SERVICE_DISABLED ? Kind.Disabled : Kind.Stopped;
            }
            finally
            {
                CloseServiceHandle(h);
            }
        }
        finally
        {
            CloseServiceHandle(scm);
        }
    }

    /// <summary>dwCurrentState (1 stopped … 4 running), and the stop it accepts; 0 if it can't be read.</summary>
    public static uint CurrentState(IntPtr service) => Status(service).State;

    public static (uint State, uint ControlsAccepted) Status(IntPtr service)
    {
        uint* status = stackalloc uint[9]; // SERVICE_STATUS_PROCESS
        if (!QueryServiceStatusEx(service, 0, status, 36, out _)) return (0, 0);
        return (status[1], status[2]);
    }

    /// <summary>The start type (2 automatic, 3 manual, 4 disabled), or -1.</summary>
    public static int StartType(IntPtr service)
    {
        const int size = 8192;
        var buffer = stackalloc byte[size];
        if (!QueryServiceConfigW(service, buffer, size, out _)) return -1;
        return (int)((uint*)buffer)[1]; // QUERY_SERVICE_CONFIGW: dwServiceType, dwStartType, …
    }

    [LibraryImport("advapi32.dll", EntryPoint = "OpenSCManagerW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    public static partial IntPtr OpenSCManagerW(string? machine, string? database, uint access);

    [LibraryImport("advapi32.dll", EntryPoint = "OpenServiceW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    public static partial IntPtr OpenServiceW(IntPtr scm, string name, uint access);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryServiceStatusEx(IntPtr service, int level, uint* buffer, int size, out int needed);

    [LibraryImport("advapi32.dll", EntryPoint = "QueryServiceConfigW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryServiceConfigW(IntPtr service, byte* buffer, int size, out int needed);

    [LibraryImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseServiceHandle(IntPtr handle);
}
