using Binder.Library;
using Binder.Models;
using Binder.ViewModels;
using Binder.Views;

namespace Binder;

/// <summary>
/// Giving games their number for game shortcuts (spec §26): the cover's menu and a cover dropped on a number (the
/// shelf), Game detail's card and Settings' list all end here. Pressing the numbers is the app's (App.GameKeys).
/// </summary>
public sealed partial class MainWindow
{
    void IShelfActions.SetGameKey(string gameId, int? number) => SetGameKey(gameId, number);
    void IDetailActions.SetGameKey(string gameId, int? number) => SetGameKey(gameId, number);
    void ISettingsActions.SetGameKey(string gameId, int? number) => SetGameKey(gameId, number);
    IReadOnlyList<Game> IDetailActions.Library => _games;

    Game? ISettingsActions.LastPlayedGame() =>
        GameKeys.LastPlayedGame(_games.Where(g => !g.Hidden && App.Settings.IsSourceEnabled(g.Source)).ToList(), _sessions);

    /// <summary>
    /// Gives the game the number (1–9) or takes its number away (null), saves, and says what happened; a game that
    /// had the number has none now, and the toast names it.
    /// </summary>
    private void SetGameKey(string gameId, int? number)
    {
        var slots = App.Settings.GameKeySlots ??= []; // "null" in a hand-edited settings.json
        if (FindGame(gameId) is not { } game || GameKeys.SlotOf(slots, gameId) == number) return;
        var lost = GameKeys.Assign(slots, gameId, number);
        SaveSettings();
        RefreshViews();
        if (number is not { } n) return;
        var detail = lost is not null && FindGame(lost) is { } other
            ? Strings.Format(Strings.GameKeyTaken, other.Name)
            : Format.GameKeyLabel(App.Settings.Hotkey, n);
        ShowToast(Strings.Format(Strings.GameKeyGiven, game.Name, n), detail, Strings.Ok);
    }

    /// <summary>Games that left the library free their number (spec §26).</summary>
    private static void ForgetGameKeys(IEnumerable<Game> dropped)
    {
        var slots = App.Settings.GameKeySlots;
        if (slots is null || slots.Count == 0) return;
        bool changed = false;
        foreach (var game in dropped)
            if (GameKeys.SlotOf(slots, game.Id) is not null)
            {
                GameKeys.Assign(slots, game.Id, null);
                changed = true;
            }
        if (changed) SaveSettings();
    }
}
