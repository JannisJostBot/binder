namespace Binder.Models;

public sealed class Session
{
    public string GameId { get; set; } = "";
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }

    // Written by the watcher since milestone 11 (null/false in older sessions): how the game ended.
    /// <summary>The game process's exit code (the most telling one when several ended).</summary>
    public uint? ExitCode { get; set; }
    /// <summary>That process's exe file name (no path).</summary>
    public string? Exe { get; set; }
    /// <summary>An engine crash reporter was still running after the game exited.</summary>
    public bool CrashReporter { get; set; }
    /// <summary>A part of a game session after the PC slept (it didn't start with the game).</summary>
    public bool Resumed { get; set; }

    // "End a stuck game" (spec §28): the user ended the game by holding the shortcut, and what Windows said then.
    /// <summary>Ended by the user with the hold.</summary>
    public bool EndedByUser { get; set; }
    /// <summary>Windows called the game's window hung (not responding) when it was ended.</summary>
    public bool Hung { get; set; }
    /// <summary>Windows Error Reporting was holding the game (it had crashed) when it was ended.</summary>
    public bool ErrorReport { get; set; }
}
