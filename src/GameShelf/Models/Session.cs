namespace GameShelf.Models;

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
}
