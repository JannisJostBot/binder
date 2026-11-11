using GameShelf.Shared;

namespace GameShelf.Views;

/// <summary>
/// Game mode's results as the widget words them. The watcher and the helper record what they changed in English
/// item names (journal, log, the result file); only the widget shows them, so the text and its translations live
/// here and the watcher stays small.
/// </summary>
public static class GameModeText
{
    /// <summary>
    /// The toast's two lines: after a restore (<paramref name="applied"/> false) "Windows is back to normal" or
    /// "Restored 4 of 5"; after the start "Game mode is on", with what it changed or couldn't change.
    /// </summary>
    public static (string Title, string Detail) Toast(RestoreResult result, bool applied)
    {
        string changes = Strings.Plural(result.Restored, Strings.GmChangeCountOne, Strings.GmChangeCount);
        if (!applied)
            return result.Success
                ? (Strings.GmBackToNormal, Strings.Format(Strings.GmPutBack, changes))
                : (Strings.Format(Strings.GmRestoredOf, result.Restored, result.Total), Strings.Format(Strings.GmCouldntPutBack, Names(result)));
        if (!result.Success)
            return (result.Restored > 0 ? Strings.GmIsOn : Strings.GmCouldntChange, Strings.Format(Strings.GmCouldntBeChanged, Names(result)));
        return (Strings.GmIsOn, result.Restored == 0 ? Strings.GmAlreadySet : Strings.Format(Strings.GmChangesUntilClose, changes));
    }

    /// <summary>"Spotify", "Spotify and Discord", "Spotify and 2 more".</summary>
    public static string Names(RestoreResult result)
    {
        var names = result.Failed.Select(ItemName).ToList();
        return names.Count switch
        {
            0 => "",
            1 or 2 => Strings.List(names),
            _ => Strings.Format(Strings.ListAnd, names[0], Strings.Format(Strings.MoreCount, names.Count - 1)),
        };
    }

    /// <summary>A Game mode item as the watcher or helper names it ("Power mode"), in the display language; app names as they are.</summary>
    public static string ItemName(string name) => name switch
    {
        "Power mode" => Strings.GmPowerMode,
        "Game Bar recording" => Strings.GmGameBar,
        "Game mode" => Strings.GameMode,
        "Services and network" => Strings.GmServicesNetwork,
        "Windows Update limit" => Strings.GmUpdateLimit,
        "Network priority" => Strings.GmNetworkPriority,
        "Windows Search" => Strings.ServiceSearch,
        "Telemetry" => Strings.ServiceTelemetry,
        "Print Spooler" => Strings.ServiceSpooler,
        _ => name,
    };

    /// <summary>"Balanced", "Best performance", … as Settings › Power names it.</summary>
    public static string Power(PowerSetting setting) => setting switch
    {
        PowerSetting.HighPerformancePlan => Strings.PowerHighPlan,
        PowerSetting.UltimatePlan => Strings.PowerUltimatePlan,
        PowerSetting.CustomPlan => Strings.PowerCustomPlan,
        PowerSetting.BestPerformance => Strings.PowerBestPerformance,
        PowerSetting.BestEfficiency => Strings.PowerBestEfficiency,
        _ => Strings.PowerBalanced,
    };
}
