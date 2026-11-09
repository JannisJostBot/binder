using GameShelf.Models;
using GameShelf.Services;
using GameShelf.ViewModels;

namespace GameShelf.Tests;

/// <summary>The view models, without any UI: what the user's changes write and report, and what filling a page doesn't.</summary>
static partial class Tests
{
    /// <summary>Records what the Settings page asked for.</summary>
    sealed class SettingsActionsSpy : ISettingsActions
    {
        public readonly List<SettingChange> Changes = [];
        public readonly List<string> Calls = [];
        public void GoBack() => Calls.Add("back");
        public void SettingChanged(SettingChange change) => Changes.Add(change);
        public void Refresh() => Calls.Add("refresh");
        public void Unhide(Game game) => Calls.Add("unhide " + game.Name);
        public void ImportShortcuts() => Calls.Add("import");
        public void RestoreShortcuts() => Calls.Add("restore");
        public void AddGame() => Calls.Add("add");
        public void RunSetup() => Calls.Add("setup");
        public void OpenFreeSpace() => Calls.Add("free space");
        public void OpenGameMode() => Calls.Add("game mode");
    }

    static void SettingsViewModelTests()
    {
        var spy = new SettingsActionsSpy();
        var vm = new SettingsViewModel(spy);
        var settings = new Settings();
        var games = new List<Game>
        {
            new() { Id = "a", Name = "Alpha", Source = GameSource.Steam },
            new() { Id = "b", Name = "Beta", Source = GameSource.Epic, Hidden = true },
            new() { Id = "c", Name = "Gamma", Source = GameSource.Gog },
        };
        var monitors = new List<MonitorInfo>
        {
            new(1, @"\\.\DISPLAY2", new PixelRect(1920, 0, 3840, 1080), new PixelRect(1920, 0, 3840, 1040), false, 1),
            new(2, @"\\.\DISPLAY1", new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), true, 1),
        };
        var notified = new List<string?>();
        vm.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        vm.Load(settings, games, autostartOn: false, refreshing: false, importedShortcuts: 0, monitors, @"\\.\DISPLAY2", hotkeyFree: true);
        Check(spy.Changes.Count == 0 && spy.Calls.Count == 0, "settings: filling the page reports no change");
        Check(notified.Count == 1 && notified[0] == string.Empty, "settings: filling the page refreshes every binding once");
        Check(vm.LibraryText == "9 of 9 on · 2 games", "settings: library line counts sources and shown games");
        Check(vm.HiddenGames.Count == 1 && vm.HiddenGames[0].Name == "Beta" && vm.HiddenCountText == "1 hidden" && !vm.NoHiddenGames,
            "settings: hidden games listed");
        Check(vm.Screens.Count == 2 && vm.Screens[0] == "1 (main) · 1920 × 1080" && vm.ScreenIndex == 1,
            "settings: screens left to right, the current one selected");
        Check(vm.DensityIndex == 1 && vm.PositionIndex == 1 && vm.DelayIndex == 2 && vm.ModeIndex == 0,
            "settings: choices show the defaults (Medium, Right edge, 10 s, Flyout)");

        // A control echoing the value it was given is not a change.
        vm.StayReady = settings.StayReady;
        vm.DensityIndex = vm.DensityIndex;
        vm.ScreenIndex = -1;
        vm.DelayIndex = -1;
        Check(spy.Changes.Count == 0, "settings: same value or no selection reports nothing");

        vm.DensityIndex = 3;
        Check(settings.GridDensity == GridDensity.List && spy.Changes is [SettingChange.Shelf], "settings: cover size writes List and reports Shelf");
        notified.Clear();
        vm.KeepLoadedIndex = 0;
        Check(settings.KeepPanelLoaded && spy.Changes[^1] == SettingChange.Behavior && notified.Contains(nameof(SettingsViewModel.StayReadyText)),
            "settings: Open faster writes KeepPanelLoaded and refreshes its texts");
        vm.EpicEnabled = false;
        Check(!settings.EpicEnabled && spy.Changes[^1] == SettingChange.Sources && vm.LibraryText == "8 of 9 on · 2 games",
            "settings: a source off writes it, reports Sources and updates the count");
        vm.ScreenIndex = 0;
        Check(settings.Monitor == @"\\.\DISPLAY1" && spy.Changes[^1] == SettingChange.Screen, "settings: picking a screen stores its device");
        vm.PositionIndex = 2;
        Check(settings.Position == DockPosition.Free && vm.PositionIsFree && spy.Changes[^1] == SettingChange.Position, "settings: Free position");
        vm.StartWithWindows = true;
        Check(settings.StartWithWindows && spy.Changes[^1] == SettingChange.Autostart, "settings: Start with Windows reported");

        vm.Load(settings, games, false, refreshing: true, importedShortcuts: 3, monitors, null, hotkeyFree: false);
        Check(vm.LibraryText == "Scanning your stores…" && !vm.CanRefresh && vm.ShortcutsText == "3 imported" && vm.CanRestoreShortcuts,
            "settings: scanning and imported shortcuts shown");
        Check(vm.HotkeyTaken && vm.HotkeyToolTip == "Taken by another app: choose a different one" && vm.ScreenIndex == -1,
            "settings: a taken shortcut and an unknown screen shown");

        vm.BackCommand.Execute(null);
        vm.Unhide(games[1]);
        vm.FreeSpaceCommand.Execute(null);
        Check(spy.Calls is ["back", "unhide Beta", "free space"], "settings: buttons reach the owner");
    }
}
