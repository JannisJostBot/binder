using GameShelf.Models;
using GameShelf.ViewModels;
using GameShelf.Views;

namespace GameShelf;

/// <summary>What the Settings page asks the panel to do (<see cref="ISettingsActions"/>).</summary>
public sealed partial class MainWindow
{
    void ISettingsActions.GoBack() => ShowShelf();
    void ISettingsActions.SettingChanged(SettingChange change) => OnSettingChanged(change);
    void ISettingsActions.Refresh() => _ = RescanAsync();
    void ISettingsActions.Unhide(Game game) => Unhide(game);
    void ISettingsActions.ImportShortcuts() => _ = ShowImportAsync();
    void ISettingsActions.RestoreShortcuts() => RestoreShortcuts();
    void ISettingsActions.AddGame() => AddGame();
    void ISettingsActions.RunSetup() => ShowSetup();
    void ISettingsActions.OpenFreeSpace() => ShowFreeSpace(new FreeSpaceState());
    void ISettingsActions.OpenGameMode() => ShowGameMode();
}
