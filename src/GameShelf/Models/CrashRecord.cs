namespace GameShelf.Models;

/// <summary>How a session ended, as far as Binder can tell (milestone 11).</summary>
public enum GameEnding { Normal, Crashed, ClosedUnexpectedly, StartFailed }

/// <summary>What the crash points at, which decides the plain-words advice. Stored by name: append freely.</summary>
public enum CrashCulprit { None, NvidiaDriver, AmdDriver, IntelDriver, GraphicsDevice, Overlay, CppRuntime, MissingRuntime, Unknown }

/// <summary>
/// The crash check of one session, kept in crashes.json (written by the widget only; the watcher writes
/// the sessions files), so the Windows logs are read once per session, and before they roll over.
/// </summary>
public sealed class CrashRecord
{
    /// <summary>With <see cref="Start"/>, the session this is about.</summary>
    public string GameId { get; set; } = "";
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }
    public GameEnding Ending { get; set; }
    public CrashCulprit Culprit { get; set; }
    public uint ExitCode { get; set; }
    /// <summary>The exception Windows logged (event 1000), else the exit code when that's a failure code.</summary>
    public uint? ErrorCode { get; set; }
    /// <summary>The faulting module's file name (no path).</summary>
    public string? Module { get; set; }
    /// <summary>The game exe's file name (no path).</summary>
    public string? Exe { get; set; }
    public bool Hang { get; set; }
    public bool DriverReset { get; set; }
    public bool CrashReporter { get; set; }
    /// <summary>The crash card was dismissed (or played again).</summary>
    public bool Seen { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsProblem => Ending != GameEnding.Normal;
}
