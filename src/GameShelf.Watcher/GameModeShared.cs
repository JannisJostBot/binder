using System.Runtime.InteropServices;

namespace GameShelf.Shared;

/// <summary>
/// Game mode (milestone 14, spec §11), the part that needs no admin rights: the item ids, the files and the
/// restore result. Compiled into both the watcher (which applies and restores) and the widget (Settings, the
/// "Restored" toast, recovery).
/// </summary>
public static class GameModeInfo
{
    public const string Power = "power";
    public const string Priority = "priority";
    public const string GameBar = "gamebar";

    /// <summary>The items the user can turn on, in the order of the Settings page.</summary>
    public static readonly string[] Items = [Power, Priority, GameBar];

    /// <summary>On by default (spec §11 table): Best performance and Above normal priority.</summary>
    public static readonly string[] DefaultItems = [Power, Priority];

    /// <summary>Held by the watcher from the first change until everything is put back.</summary>
    public const string MutexName = @"Local\GameShelf.GameMode";

    public static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameShelf");

    /// <summary>The write-ahead journal: what was changed and what it was before. Exists only while changes are applied.</summary>
    public static string JournalPath => Path.Combine(Dir, "gamemode-journal.json");

    public static string LogPath => Path.Combine(Dir, "gamemode.log");

    /// <summary>The last restore: its time on the first line, <see cref="RestoreResult.Encode"/> on the second.</summary>
    public static string LastResultPath => Path.Combine(Dir, "gamemode-last.txt");

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
