using Binder.Models;
using Binder.Services;
using Binder.ViewModels;

namespace Binder;

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
    Task<string> ISettingsActions.CheckSourceAsync(GameSource launcher) => Task.Run(() => SourceCheck.Run(launcher));

    private void OnSettingChanged(SettingChange change)
    {
        var settings = App.Settings;
        switch (change)
        {
            case SettingChange.DisplayMode:
                SaveSettings();
                App.Current.ApplyDisplayMode(); // rebuilds the panel window in the new mode; this one closes
                return;
            case SettingChange.Hotkey:
                App.Current.ApplyHotkey();
                break;
            case SettingChange.TrayIcon:
                App.Current.ApplyTrayIcon();
                break;
            case SettingChange.Position:
                if (settings.Position == DockPosition.Free) RememberFreePosition(); // start where it is now
                PlaceOnScreen();
                break;
            case SettingChange.Screen:
                if (settings.Position == DockPosition.Free) settings.Position = DockPosition.Right; // dock on the new screen
                PlaceOnScreen();
                break;
            case SettingChange.DesktopLayer:
                if (_desktopLayer is not null) _desktopLayer.Enabled = settings.StayOnDesktop;
                break;
            case SettingChange.Autostart:
                if (!Autostart.TrySet(settings.StartWithWindows))
                    ShowToast(Strings.AutostartFailed, Strings.AutostartFailedDetail, Strings.Ok);
                settings.StartWithWindows = Autostart.IsEnabled();
                break;
            case SettingChange.SourcePaths or SettingChange.SourceArtwork:
                // Used from the next scan (Refresh or the next start); "Check" tries them at once.
                Library.SourceDefaults.Use(settings.SourceOverrides);
                if (change == SettingChange.SourceArtwork) CoverCache.ForgetSteamLookups();
                SaveSettings();
                return; // nothing on the page or the shelf changes until then
            case SettingChange.StartMenu:
                SyncStartMenu(); // writes or removes the folder off the UI thread, then saves
                return;
            case SettingChange.Updates:
                SaveSettings();
                App.Current.UpdateModeChanged(); // the card and the "…" dot follow through UpdateChanged
                return;
            case SettingChange.Sources:
                SaveSettings();
                RefreshViews();
                _ = RescanAsync(); // a re-enabled store may have new games
                return;
        }
        SaveSettings();
        RefreshViews();
    }
}
