using Binder.Library;
using Binder.Models;
using Binder.Services;
using Binder.ViewModels;
using Binder.Views;

namespace Binder;

/// <summary>What the shelf asks the panel to do (<see cref="IShelfActions"/>).</summary>
public sealed partial class MainWindow
{
    void IShelfActions.Play(Game game) => Play(game);
    void IShelfActions.OpenDetails(Game game) => ShowDetail(game);
    void IShelfActions.Act(Game game, GameAction action) => OnGameAction(game, action);
    void IShelfActions.OpenSettings() => ShowSettings();
    void IShelfActions.ImportShortcuts() => _ = ShowImportAsync();
    void IShelfActions.AddGame() => AddGame();
    void IShelfActions.SetGameMode(bool on) => SetGameMode(on);
    void IShelfActions.OpenGameMode() => OpenGameMode();
    void IShelfActions.OpenFreeSpace() => OpenFreeSpace();
    void IShelfActions.Refresh() => _ = RescanAsync();
    void IShelfActions.ViewSettingsChanged() => SaveSettings();
    bool IShelfActions.HelperNeedsUpdate(IReadOnlyList<Game> games) => GameModeService.HelperNeedsUpdate(App.Settings, games);
    bool IShelfActions.GameModeRunning => GameModeService.Pending;
    Task IShelfActions.UpdateHelperAsync() => UpdateGameModeHelperAsync();
    bool IShelfActions.CanOpenLauncher(GameSource source) => GameActions.CanOpenLauncher(source);
    void IShelfActions.OpenLauncher(GameSource source) => GameActions.OpenLauncher(source);

    void IShelfActions.TogglePin()
    {
        App.Pinned = !App.Pinned;
        Shelf.ViewModel!.SetFlyout(IsFlyout, App.Pinned);
    }

    /// <summary>
    /// Proposals §13 c and e: each time the panel opens, "Welcome back" shows the session that ended since the
    /// last one shown (2 min or longer), with a playtime milestone it passed. Stored as shown right away, so the
    /// next opening shows the normal slot. The first run only records what's there, showing nothing.
    /// </summary>
    private void PickWelcome()
    {
        var settings = App.Settings;
        if (settings.WelcomeBackSessionStart is not { } shownFor)
        {
            settings.WelcomeBackSessionStart = Moments.Seed(_sessions, _games);
            SaveSettings();
            SaveLibrary();
            Shelf.ViewModel!.SetWelcome(null);
            return;
        }
        var moment = Moments.Pick(_sessions, shownFor, id => FindGame(id)?.MilestoneShownHours ?? 0, DateTime.Now);
        if (moment is not null)
        {
            settings.WelcomeBackSessionStart = moment.SessionStart;
            SaveSettings();
            if (moment.Milestone > 0 && FindGame(moment.GameId) is { } game)
            {
                game.MilestoneShownHours = moment.Milestone;
                SaveLibrary();
            }
        }
        Shelf.ViewModel!.SetWelcome(moment);
    }
}
