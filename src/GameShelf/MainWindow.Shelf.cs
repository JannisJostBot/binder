using GameShelf.Models;
using GameShelf.Services;
using GameShelf.ViewModels;
using GameShelf.Views;

namespace GameShelf;

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
    Task IShelfActions.UpdateHelperAsync() => UpdateGameModeHelperAsync();

    void IShelfActions.TogglePin()
    {
        App.Pinned = !App.Pinned;
        Shelf.ViewModel!.SetFlyout(IsFlyout, App.Pinned);
    }
}
