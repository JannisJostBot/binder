using System.Globalization;
using System.Text;
using Binder.Models;
using Binder.Views;

namespace Binder.Library;

/// <summary>One entry from the Windows event logs, reduced to what the crash check reads.</summary>
public sealed record WindowsEvent(int Id, string Provider, DateTimeOffset Time, IReadOnlyDictionary<string, string> Data);

/// <summary>The PC facts in a bug report or AI prompt (no user name, no paths).</summary>
public sealed record PcFacts(string Windows, IReadOnlyList<string> Graphics, int MemoryGb);

/// <summary>
/// Milestone 11: decides how a session ended from the watcher's exit code and the Windows logs, and puts it in
/// plain words. Pure (no I/O), so it's unit tested; the logs are read by <c>CrashLog</c>.
/// </summary>
public static class CrashAnalysis
{
    public const int ApplicationError = 1000, ApplicationHang = 1002, DisplayReset = 4101;

    /// <summary>A session shorter than this that ended with an error "closed right after starting".</summary>
    public static readonly TimeSpan StartWindow = TimeSpan.FromSeconds(5);

    /// <summary>Windows writes the crash event around the moment the game dies; look a little past the end.</summary>
    public static readonly TimeSpan EventSlack = TimeSpan.FromMinutes(2);

    private const uint ControlCExit = 0xC000013A; // console window closed: not a crash
    private const uint DeadExit = 0xDEAD; // 57005: a deliberate exit on a fatal error (seen from Wardogs)

    /// <summary>
    /// A failure code: NTSTATUS errors (0xC…) and HRESULT failures (0x8…) both have the high bit set, and 0xDEAD
    /// is what some games exit with on a fatal error. Other plain nonzero codes (1, 2, …) aren't: plenty of
    /// games exit with them normally.
    /// </summary>
    public static bool IsFailureCode(uint code) => ((code & 0x80000000) != 0 && code != ControlCExit) || code == DeadExit;

    /// <summary>Whether a session is worth reading the Windows logs for (most aren't, so most cost nothing).</summary>
    public static bool NeedsCheck(Session s) =>
        s.ExitCode is { } code && (IsFailureCode(code) || (code != 0 && s.End - s.Start < StartWindow)) || s.CrashReporter;

    /// <summary>The check's record for a session (same game and start), or null when it wasn't checked.</summary>
    public static CrashRecord? For(IReadOnlyList<CrashRecord> records, Session s) =>
        records.FirstOrDefault(r => r.GameId == s.GameId && r.Start.UtcTicks == s.Start.UtcTicks);

    /// <summary>Classifies one session from its exit code and the events in its time window.</summary>
    public static CrashRecord Analyze(Session s, string? installFolder, IReadOnlyList<WindowsEvent> events)
    {
        uint code = s.ExitCode ?? 0;
        var from = s.Start - TimeSpan.FromSeconds(5);
        var to = s.End + EventSlack;
        var prefix = string.IsNullOrEmpty(installFolder) ? null : installFolder.TrimEnd('\\') + "\\";

        bool Ours(WindowsEvent e, string pathKey)
        {
            if (prefix is not null && e.Data.TryGetValue(pathKey, out var path) && !string.IsNullOrEmpty(path))
                return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            return s.Exe is not null && e.Data.TryGetValue("AppName", out var app) &&
                   app.Equals(s.Exe, StringComparison.OrdinalIgnoreCase);
        }

        var inWindow = events.Where(e => e.Time >= from && e.Time <= to).ToList();
        var crash = inWindow.Where(e => e.Id == ApplicationError && e.Provider == "Application Error" && Ours(e, "AppPath"))
            .OrderBy(e => e.Time).LastOrDefault();
        bool hang = inWindow.Any(e => e.Id == ApplicationHang && e.Provider == "Application Hang" && Ours(e, "ExeFileName"));
        bool driverReset = inWindow.Any(e => e.Id == DisplayReset && e.Time >= s.Start);

        uint? error = null;
        string? module = null;
        if (crash is not null)
        {
            if (crash.Data.TryGetValue("ExceptionCode", out var hex) &&
                uint.TryParse(hex.Replace("0x", "", StringComparison.OrdinalIgnoreCase), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed))
                error = parsed;
            if (crash.Data.TryGetValue("ModuleName", out var name) && name.Length > 0) module = Path.GetFileName(name);
        }
        if (error is null && IsFailureCode(code)) error = code;

        var duration = s.End - s.Start;
        var ending =
            code != 0 && !IsBenign(code) && duration < StartWindow ? GameEnding.StartFailed
            : IsFailureCode(code) && (crash is not null || hang || s.CrashReporter) ? GameEnding.Crashed
            : IsFailureCode(code) ? GameEnding.ClosedUnexpectedly
            : GameEnding.Normal;

        var culprit = ending == GameEnding.Normal ? CrashCulprit.None : CulpritOf(module, error, driverReset, ending, crash, prefix);

        return new CrashRecord
        {
            GameId = s.GameId,
            Start = s.Start,
            End = s.End,
            Ending = ending,
            Culprit = culprit,
            ExitCode = code,
            ErrorCode = error,
            Module = module,
            Exe = s.Exe,
            Hang = hang,
            DriverReset = driverReset,
            CrashReporter = s.CrashReporter,
            Seen = ending == GameEnding.Normal,
        };
    }

    private static bool IsBenign(uint code) => code == ControlCExit;

    private static readonly uint[] MissingRuntimeCodes =
    [
        0xC0000135, // a DLL wasn't found
        0xC000007B, // a 32/64-bit mismatch
        0xC0000142, // a DLL failed to initialize
        0xC0000139, // an entry point wasn't found
    ];

    private static CrashCulprit CulpritOf(string? module, uint? error, bool driverReset, GameEnding ending, WindowsEvent? crash, string? prefix)
    {
        if (error is { } e && MissingRuntimeCodes.Contains(e)) return CrashCulprit.MissingRuntime;
        if (module is not null)
        {
            var m = module.ToLowerInvariant();
            if (StartsWithAny(m, "nvwgf2um", "nvoglv", "nvd3dum", "nvgpucomp", "nvldumd", "nvlddmkm", "nvcuda")) return CrashCulprit.NvidiaDriver;
            if (StartsWithAny(m, "amdxx", "amdxc", "amdvlk", "amdihk", "atiumd", "atidxx", "aticfx", "atio6axx")) return CrashCulprit.AmdDriver;
            if (StartsWithAny(m, "igd", "igc64", "igvk", "igxelp", "intelocl")) return CrashCulprit.IntelDriver;
            if (StartsWithAny(m, "gameoverlayrenderer", "discordhook", "discord_hook", "rtsshooks", "graphics-hook", "reshade", "eosovh", "overlay64"))
                return CrashCulprit.Overlay;
            // A dxgi.dll in the game's own folder is ReShade or another injected mod, not Windows' own.
            if (m == "dxgi.dll" && prefix is not null && crash is not null &&
                crash.Data.TryGetValue("ModulePath", out var path) && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return CrashCulprit.Overlay;
            if (StartsWithAny(m, "vcruntime", "msvcp", "ucrtbase", "concrt")) return CrashCulprit.CppRuntime;
        }
        if (error is 0x887A0005 or 0x887A0006 or 0x887A0007 || driverReset) return CrashCulprit.GraphicsDevice;
        return CrashCulprit.Unknown;
    }

    private static bool StartsWithAny(string value, params string[] prefixes)
    {
        foreach (var p in prefixes) if (value.StartsWith(p, StringComparison.Ordinal)) return true;
        return false;
    }

    // ---- Restart after a crash (spec §17) -----------------------------------------------------------------

    /// <summary>A crash is restarted only after a session at least this long (the watcher's rule too).</summary>
    public static readonly TimeSpan RestartMinSession = TimeSpan.FromSeconds(60);

    /// <summary>Crashed sessions this close together count as "in a row".</summary>
    public static readonly TimeSpan RestartWindow = TimeSpan.FromMinutes(30);

    public const int MaxRestarts = 2;

    /// <summary>
    /// Crashed or closed unexpectedly after at least a minute. Never a failed start (a missing runtime or a broken
    /// install; restarting won't help) or a normal exit.
    /// </summary>
    public static bool IsRestartable(CrashRecord r) =>
        r.Ending is GameEnding.Crashed or GameEnding.ClosedUnexpectedly && r.End - r.Start >= RestartMinSession;

    /// <summary>
    /// The game's restartable crashes in a row up to and including <paramref name="latest"/>: its sessions newest
    /// first, back to the first that ended otherwise or more than 30 minutes before. From the stored records only,
    /// so nothing has to be kept while the game runs.
    /// </summary>
    public static int CrashesInARow(IReadOnlyList<Session> sessions, IReadOnlyList<CrashRecord> records, CrashRecord latest)
    {
        int count = 0;
        foreach (var s in sessions.Where(s => s.GameId == latest.GameId && s.Start <= latest.Start).OrderByDescending(s => s.Start))
        {
            if (latest.End - s.End > RestartWindow) break;
            if (For(records, s) is not { } r || !IsRestartable(r)) break;
            count++;
        }
        return Math.Max(1, count); // latest itself, even if its session isn't loaded
    }

    /// <summary>Whether to restart the game after <paramref name="latest"/>: its toggle is on and it's not the third crash in a row.</summary>
    public static bool ShouldRestart(Game game, CrashRecord latest, IReadOnlyList<Session> sessions, IReadOnlyList<CrashRecord> records) =>
        game.RestartOnCrash && IsRestartable(latest) && CrashesInARow(sessions, records, latest) <= MaxRestarts;

    /// <summary>The crash card's "Restarted twice already" line: it would have restarted, but not again.</summary>
    public static bool RestartLimitReached(Game game, CrashRecord latest, IReadOnlyList<Session> sessions, IReadOnlyList<CrashRecord> records) =>
        game.RestartOnCrash && IsRestartable(latest) && CrashesInARow(sessions, records, latest) > MaxRestarts;

    // ---- Plain words -----------------------------------------------------------------------------------

    /// <summary>"[Game] crashed after 1 h 12 min", "… closed right after starting", …</summary>
    public static string Title(string game, CrashRecord r) => r.Ending switch
    {
        GameEnding.StartFailed => Strings.Format(Strings.CrashTitleStart, game),
        GameEnding.ClosedUnexpectedly => Strings.Format(Strings.CrashTitleClosed, game, Format.Duration(r.End - r.Start)),
        _ when r.Hang => Strings.Format(Strings.CrashTitleHang, game, Format.Duration(r.End - r.Start)),
        _ => Strings.Format(Strings.CrashTitleCrash, game, Format.Duration(r.End - r.Start)),
    };

    /// <summary>One or two sentences: what it points at and what usually helps. Nothing made up for unknown errors.</summary>
    public static string Explanation(CrashRecord r) => r.Culprit switch
    {
        CrashCulprit.NvidiaDriver => Strings.Format(Strings.CrashInDriverOverclock, "NVIDIA"),
        CrashCulprit.AmdDriver => Strings.Format(Strings.CrashInDriverOverclock, "AMD"),
        CrashCulprit.IntelDriver => Strings.Format(Strings.CrashInDriver, "Intel"),
        CrashCulprit.GraphicsDevice => Strings.CrashGpu,
        CrashCulprit.Overlay => Strings.Format(Strings.CrashOverlay, r.Module),
        CrashCulprit.CppRuntime => Strings.CrashCppRuntime,
        CrashCulprit.MissingRuntime => Strings.CrashMissingRuntime,
        _ when r.Ending == GameEnding.ClosedUnexpectedly => Strings.CrashExitCode,
        _ => Strings.CrashUnknown,
    };

    /// <summary>The AI prompt is offered only when Binder has no advice of its own.</summary>
    public static bool OffersAiPrompt(CrashRecord r) => r.Culprit == CrashCulprit.Unknown;

    /// <summary>The Visual C++ Redistributable download page, for the runtime cases.</summary>
    public const string RedistUrl = "https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist";

    public static bool OffersRedist(CrashRecord r) => r.Culprit is CrashCulprit.MissingRuntime or CrashCulprit.CppRuntime;

    /// <summary>"Access violation" for 0xC0000005, and so on; null when there's no plain name for it.</summary>
    public static string? CodeName(uint code) => code switch
    {
        0xC0000005 => Strings.CodeAccessViolation,
        0xC0000409 => Strings.CodeStackBuffer,
        0xC0000374 => Strings.CodeHeapCorruption,
        0xC00000FD => Strings.CodeStackOverflow,
        0xC0000017 or 0xC000009A => Strings.CodeOutOfMemory,
        0xC000001D => Strings.CodeIllegalInstruction,
        0xC0000094 => Strings.CodeDivideByZero,
        0x80000003 => Strings.CodeBreakpoint,
        0xE06D7363 => Strings.CodeCppException,
        0xC000027B => Strings.CodeAppError,
        0xC0000135 => Strings.CodeDllNotFound,
        0xC000007B => Strings.CodeBadImage,
        0xC0000142 => Strings.CodeDllInitFailed,
        0xC0000139 => Strings.CodeEntryPointNotFound,
        0x887A0005 => Strings.CodeGpuRemoved,
        0x887A0006 => Strings.CodeGpuHung,
        0x887A0007 => Strings.CodeGpuReset,
        0xFFFFFFFF => Strings.CodeMinusOne,
        _ => null,
    };

    public static string Hex(uint code) => "0x" + code.ToString("X8", CultureInfo.InvariantCulture);

    /// <summary>"Access violation (0xC0000005)", or just the code.</summary>
    public static string WhatHappened(CrashRecord r)
    {
        if (r.Hang && r.ErrorCode is null) return Strings.CrashHangClosed;
        uint code = r.ErrorCode ?? r.ExitCode;
        return CodeName(code) is { } name ? Strings.Format(Strings.CodeWithName, name, Hex(code)) : Strings.Format(Strings.ErrorCode, Hex(code));
    }

    /// <summary>"nvwgf2umx.dll · NVIDIA graphics driver", the module alone, or the game's exe.</summary>
    public static string? Where(CrashRecord r)
    {
        var place = r.Module ?? r.Exe;
        if (place is null) return null;
        return CulpritName(r.Culprit) is { } name && r.Module is not null ? Strings.Format(Strings.Dot, place, name) : place;
    }

    public static string? CulpritName(CrashCulprit c) => c switch
    {
        CrashCulprit.NvidiaDriver => Strings.Format(Strings.GraphicsDriverOf, "NVIDIA"),
        CrashCulprit.AmdDriver => Strings.Format(Strings.GraphicsDriverOf, "AMD"),
        CrashCulprit.IntelDriver => Strings.Format(Strings.GraphicsDriverOf, "Intel"),
        CrashCulprit.GraphicsDevice => Strings.GraphicsCard,
        CrashCulprit.Overlay => Strings.OverlayOrMod,
        CrashCulprit.CppRuntime => Strings.CppRuntime,
        CrashCulprit.MissingRuntime => Strings.CppRuntime,
        _ => null,
    };

    /// <summary>The other signs at the time: a display driver reset, a hang, an engine crash reporter.</summary>
    public static string? AlsoAtThatTime(CrashRecord r)
    {
        var parts = new List<string>();
        if (r.DriverReset) parts.Add(Strings.AlsoDriverReset);
        if (r.Hang && r.ErrorCode is not null) parts.Add(Strings.AlsoHang);
        if (r.CrashReporter) parts.Add(Strings.AlsoCrashReporter);
        return parts.Count == 0 ? null : string.Join(". ", parts);
    }

    /// <summary>The same kind of trouble in one family, for the pattern line.</summary>
    private static string? Family(CrashCulprit c) => c switch
    {
        CrashCulprit.NvidiaDriver or CrashCulprit.AmdDriver or CrashCulprit.IntelDriver or CrashCulprit.GraphicsDevice => Strings.FamilyDriver,
        CrashCulprit.Overlay => Strings.FamilyOverlay,
        CrashCulprit.CppRuntime or CrashCulprit.MissingRuntime => Strings.FamilyRuntime,
        _ => null,
    };

    /// <summary>
    /// "2 crashes in the last 10 sessions, both in the graphics driver." Null unless at least two of the
    /// sessions (newest first, at most 10 used) ended badly.
    /// </summary>
    public static string? Pattern(IReadOnlyList<CrashRecord?> recent)
    {
        var last = recent.Take(10).ToList();
        var bad = last.Where(r => r is { IsProblem: true }).Select(r => r!).ToList();
        if (bad.Count < 2) return null;
        var family = Family(bad[0].Culprit);
        if (family is not null && bad.All(r => Family(r.Culprit) == family))
            return Strings.Format(bad.Count == 2 ? Strings.PatternBothIn : Strings.PatternAllIn, bad.Count, last.Count, family);
        return Strings.Format(Strings.Pattern, bad.Count, last.Count);
    }

    // ---- The two texts to copy ---------------------------------------------------------------------------

    private static string LongDuration(TimeSpan d) =>
        d.TotalSeconds < 60 ? Strings.AFewSeconds :
        d.TotalMinutes < 90 ? Strings.Format(Strings.AboutMinutes, Math.Max(1, (int)Math.Round(d.TotalMinutes))) :
        Strings.Format(Strings.AboutHours, (int)Math.Round(d.TotalHours));

    /// <summary>"0xC0000005 (Access violation) in the file x.dll", or with <paramref name="report"/> "… in x.dll".</summary>
    private static string CodeSentence(CrashRecord r, bool report = false)
    {
        uint code = r.ErrorCode ?? r.ExitCode;
        var name = CodeName(code);
        var where = r.Module ?? r.Exe;
        var text = name is null ? Hex(code) : Strings.Format(Strings.CodeWithName, Hex(code), name);
        return where is null ? text : Strings.Format(report ? Strings.CodeIn : Strings.CodeInFile, text, where);
    }

    private static string Pc(PcFacts pc, string separator)
    {
        var parts = new List<string> { pc.Windows };
        parts.AddRange(pc.Graphics);
        if (pc.MemoryGb > 0) parts.Add(Strings.Format(Strings.MemoryGb, pc.MemoryGb));
        return string.Join(separator, parts);
    }

    private static string HowOften(int problems, int sessions) =>
        problems <= 1 ? Strings.Format(Strings.PromptFirstTime, sessions) : Strings.Format(Strings.PromptTimes, problems, sessions);

    /// <summary>A first-person question for any AI assistant (board 13). No user name, no paths.</summary>
    public static string AiPrompt(string game, CrashRecord r, PcFacts pc, int problemsInRecent, int recentSessions)
    {
        var sb = new StringBuilder();
        var played = LongDuration(r.End - r.Start);
        sb.Append(r.Ending switch
        {
            GameEnding.StartFailed => Strings.Format(Strings.PromptStartFailed, game),
            GameEnding.ClosedUnexpectedly => Strings.Format(Strings.PromptClosed, game, played),
            _ => Strings.Format(Strings.PromptCrashed, game, played),
        });
        sb.Append('\n').Append(Strings.Format(Strings.PromptReported, CodeSentence(r)));
        if (AlsoAtThatTime(r) is { } also) sb.Append('\n').Append(Strings.Format(Strings.PromptAlso, Strings.LowerFirst(also)));
        sb.Append('\n').Append(Strings.Format(Strings.PromptPc, Pc(pc, ", ")));
        sb.Append('\n').Append(Strings.Format(Strings.PromptHowOften, HowOften(problemsInRecent, recentSessions)));
        sb.Append('\n').Append(Strings.PromptQuestion);
        return sb.ToString();
    }

    /// <summary>The same facts as a short report for the game's developer. No user name, no paths.</summary>
    public static string BugReport(string game, string? store, CrashRecord r, PcFacts pc, int problemsInRecent, int recentSessions)
    {
        var when = r.End.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        var length = Format.Duration(r.End - r.Start);
        var what = r.Ending switch
        {
            GameEnding.StartFailed => Strings.Format(Strings.ReportStartFailed, when),
            GameEnding.ClosedUnexpectedly => Strings.Format(Strings.ReportClosed, length, when),
            _ when r.Hang => Strings.Format(Strings.ReportHang, length, when),
            _ => Strings.Format(Strings.ReportCrashed, length, when),
        };
        var sb = new StringBuilder();
        sb.Append(Strings.Format(Strings.LabelValue, Strings.ReportGame, store is null ? game : Strings.Format(Strings.ReportGameStore, game, store))).Append('\n');
        sb.Append(Strings.Format(Strings.LabelValue, Strings.ReportWhat, what)).Append('\n');
        sb.Append(Strings.Format(Strings.LabelValue, Strings.ReportError, CodeSentence(r, report: true))).Append('\n');
        if (AlsoAtThatTime(r) is { } also) sb.Append(Strings.Format(Strings.LabelValue, Strings.ReportAlso, also)).Append('\n');
        sb.Append(Strings.Format(Strings.LabelValue, Strings.ReportHowOften, problemsInRecent <= 1
            ? Strings.Format(Strings.ReportFirstTime, recentSessions) : Strings.Format(Strings.ReportTimes, problemsInRecent, recentSessions))).Append('\n');
        sb.Append(Strings.Format(Strings.LabelValue, Strings.ReportPc, Pc(pc, " · "))).Append('\n');
        sb.Append(Strings.ReportFooter);
        return sb.ToString();
    }

    // ---- Parsing what Windows gives us -----------------------------------------------------------------

    /// <summary>
    /// NVIDIA's Windows driver version 32.0.15.6636 is its own 566.36 (the last five digits). Others are shown as
    /// Windows reports them.
    /// </summary>
    public static string DriverVersion(string adapter, string version)
    {
        if (!adapter.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) return version;
        var parts = version.Split('.');
        if (parts.Length != 4) return version;
        var digits = parts[2] + parts[3].PadLeft(4, '0');
        if (digits.Length < 5 || !digits.All(char.IsAsciiDigit)) return version;
        var five = digits[^5..];
        return $"{int.Parse(five[..3], CultureInfo.InvariantCulture)}.{five[3..]}";
    }

    private const int MaxEventXml = 64 * 1024;

    /// <summary>
    /// Reads one event as rendered by EvtRender (XML): provider, id, time and the named EventData values. Returns
    /// null for anything malformed or oversized; never throws on bad input.
    /// </summary>
    public static WindowsEvent? ParseEventXml(string xml)
    {
        if (xml.Length > MaxEventXml) return null;
        var provider = Attribute(xml, "<Provider ", "Name");
        var time = Attribute(xml, "<TimeCreated ", "SystemTime");
        var id = Element(xml, "EventID");
        if (provider is null || time is null || id is null) return null;
        if (!int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var eventId)) return null;
        if (!DateTimeOffset.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var when)) return null;

        var data = new Dictionary<string, string>(StringComparer.Ordinal);
        int at = 0;
        while (data.Count < 64 && (at = xml.IndexOf("<Data Name=", at, StringComparison.Ordinal)) >= 0)
        {
            int nameStart = at + 11;
            if (nameStart >= xml.Length) break;
            char quote = xml[nameStart];
            int nameEnd = xml.IndexOf(quote, nameStart + 1);
            if (nameEnd < 0) break;
            var name = xml[(nameStart + 1)..nameEnd];
            int close = xml.IndexOf('>', nameEnd);
            if (close < 0) break;
            if (xml[close - 1] == '/') { data[name] = ""; at = close; continue; } // <Data Name='x'/>
            int end = xml.IndexOf("</Data>", close, StringComparison.Ordinal);
            if (end < 0) break;
            data[name] = Unescape(xml[(close + 1)..end]);
            at = end;
        }
        return new WindowsEvent(eventId, Unescape(provider), when, data);
    }

    private static string? Attribute(string xml, string tag, string name)
    {
        int at = xml.IndexOf(tag, StringComparison.Ordinal);
        if (at < 0) return null;
        int end = xml.IndexOf('>', at);
        if (end < 0) return null;
        int attr = xml.IndexOf(name + "=", at, end - at, StringComparison.Ordinal);
        if (attr < 0) return null;
        int start = attr + name.Length + 1;
        if (start >= end) return null;
        char quote = xml[start];
        int stop = xml.IndexOf(quote, start + 1);
        return stop < 0 || stop > end ? null : xml[(start + 1)..stop];
    }

    private static string? Element(string xml, string name)
    {
        int at = xml.IndexOf("<" + name, StringComparison.Ordinal);
        if (at < 0) return null;
        int start = xml.IndexOf('>', at);
        int end = xml.IndexOf("</" + name + ">", StringComparison.Ordinal);
        return start < 0 || end < start ? null : xml[(start + 1)..end];
    }

    private static string Unescape(string s) => s.Contains('&')
        ? s.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&apos;", "'").Replace("&amp;", "&")
        : s;
}
