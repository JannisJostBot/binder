namespace Binder.Models;

/// <summary>How a session ended, as far as Binder can tell (milestone 11). Stored by name: append freely.</summary>
/// <remarks><see cref="GameEnding.EndedByUser"/>: ended with "End a stuck game" (spec §28) while Windows saw nothing wrong.</remarks>
public enum GameEnding { Normal, Crashed, ClosedUnexpectedly, StartFailed, EndedByUser }

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
    /// <summary>The user ended the game with "End a stuck game" (spec §28), whatever Windows said about it.</summary>
    public bool EndedByUser { get; set; }
    /// <summary>Windows Error Reporting was writing the crash report when the user ended it.</summary>
    public bool ErrorReport { get; set; }
    /// <summary>The session's part after the PC slept: the game ran longer than <see cref="End"/> - <see cref="Start"/>.</summary>
    public bool Resumed { get; set; }
    /// <summary>The crash card was dismissed (or played again).</summary>
    public bool Seen { get; set; }

    /// <summary>A crash, hang or failed start: counted in the crash history. A game ended while it responded isn't.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsProblem => Ending is not (GameEnding.Normal or GameEnding.EndedByUser);

    /// <summary>A card is shown for it on return: a problem, or a game the user ended.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasCard => Ending != GameEnding.Normal;
}
