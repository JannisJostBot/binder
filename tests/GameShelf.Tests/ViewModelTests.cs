using GameShelf.Library;
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
        Check(vm.Screens.Length == 2 && vm.Screens[0] == "1 (main) · 1920 × 1080" && vm.SelectedScreen == vm.Screens[1],
            "settings: screens left to right, the current one selected");
        Check(vm.DensityIndex == 1 && vm.PositionIndex == 1 && vm.DelayIndex == 2 && vm.ModeIndex == 0,
            "settings: choices show the defaults (Medium, Right edge, 10 s, Flyout)");

        // A control echoing the value it was given is not a change.
        vm.StayReady = settings.StayReady;
        vm.DensityIndex = vm.DensityIndex;
        vm.SelectedScreen = null;
        vm.SelectedScreen = "not a screen";
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
        vm.SelectedScreen = vm.Screens[0];
        Check(settings.Monitor == @"\\.\DISPLAY1" && spy.Changes[^1] == SettingChange.Screen, "settings: picking a screen stores its device");
        vm.PositionIndex = 2;
        Check(settings.Position == DockPosition.Free && vm.PositionIsFree && spy.Changes[^1] == SettingChange.Position, "settings: Free position");
        vm.StartWithWindows = true;
        Check(settings.StartWithWindows && spy.Changes[^1] == SettingChange.Autostart, "settings: Start with Windows reported");

        vm.Load(settings, games, false, refreshing: true, importedShortcuts: 3, monitors, null, hotkeyFree: false);
        Check(vm.LibraryText == "Scanning your stores…" && !vm.CanRefresh && vm.ShortcutsText == "3 imported" && vm.CanRestoreShortcuts,
            "settings: scanning and imported shortcuts shown");
        Check(vm.HotkeyTaken && vm.HotkeyToolTip == "Taken by another app: choose a different one" && vm.SelectedScreen is null,
            "settings: a taken shortcut and an unknown screen shown");

        vm.BackCommand.Execute(null);
        vm.Unhide(games[1]);
        vm.FreeSpaceCommand.Execute(null);
        Check(spy.Calls is ["back", "unhide Beta", "free space"], "settings: buttons reach the owner");
    }

    /// <summary>Records what the detail page asked for; answers its questions about Windows with fixed facts.</summary>
    sealed class DetailActionsSpy : IDetailActions
    {
        public readonly List<string> Calls = [];
        public int Edits;
        public void GoBack() => Calls.Add("back");
        public void Act(Game game, Views.GameAction action) => Calls.Add(action + " " + game.Name);
        public void GameEdited(Game game) => Edits++;
        public void CrashSeen(CrashRecord record) => Calls.Add("seen");
        public void CloseConflict(DetailConflict conflict) => Calls.Add("close " + conflict.Name);
        public void OpenLink(string url) => Calls.Add("link " + url);
        public string? InstallFolder(Game game) => game.InstallDir;
        public bool HasStorePage(Game game) => game.Source == GameSource.Steam;
        public string DescribeStart(Game game) => game.LaunchArgs is null ? "Steam" : "Steam, with " + game.LaunchArgs;
        public bool ImportCover(Game game, string path)
        {
            game.CustomCover = path;
            return true;
        }
        public void ClearCover(Game game) => game.CustomCover = null;
        public Task<DetailScan?> ScanAsync(Game game) => Task.FromResult<DetailScan?>(null);
    }

    static void DetailViewModelTests()
    {
        var spy = new DetailActionsSpy();
        var vm = new DetailViewModel(spy);
        var settings = new Settings();
        var game = new Game
        {
            Id = "steam:1", Name = "Alpha", Source = GameSource.Steam, StoreId = "1", InstallDir = @"C:\Games\Alpha",
            Engine = GameEngine.Unity, LaunchArgs = "-popupwindow -force-d3d11 -foo",
        };
        var now = DateTimeOffset.Now;
        var sessions = new List<Session>
        {
            new() { GameId = "steam:1", Start = now.AddHours(-3), End = now.AddHours(-2), ExitCode = 0 },
            new() { GameId = "steam:1", Start = now.AddHours(-1), End = now.AddMinutes(-30), ExitCode = 0xC0000005 },
            new() { GameId = "other", Start = now.AddHours(-1), End = now },
        };
        var crashes = new List<CrashRecord> { new() { GameId = "steam:1", Start = sessions[1].Start, End = sessions[1].End, Ending = GameEnding.Crashed } };
        var notified = new List<string?>();
        int newGame = 0;
        vm.PropertyChanged += (_, e) => notified.Add(e.PropertyName);
        vm.NewGameShown += () => newGame++;

        vm.Show(game, sessions, settings, crashes);
        Check(spy.Edits == 0 && spy.Calls.Count == 0, "detail: filling the page reports no change");
        Check(notified.Contains(string.Empty) && newGame == 1, "detail: a new game refreshes every binding");
        Check(vm.Title == "Alpha" && vm.Subtitle == "Steam" && vm.PlayName == "Play Alpha" && vm.HasFolder && vm.HasStorePage,
            "detail: header from the game");
        Check(vm.PlaytimeText == "1 h 30 min" && vm.LastPlayedText == Views.Format.RelativeDay(sessions[1].End) && vm.Sessions.Length == 2 && vm.HasSessions,
            "detail: playtime and sessions of this game only");
        Check(vm.Sessions[0].Problem && vm.Sessions[0].MarkLabel == "Crashed" && vm.Sessions[1].KnownNormal && vm.Sessions[1].MarkLabel == "Closed normally",
            "detail: the crashed session is marked, the other closed normally");
        Check(vm.ChartBars.Length == 8 && !vm.ShowMonths, "detail: chart of 8 weeks");
        vm.ShowMonths = true;
        Check(vm.ChartBars.Length == 12 && vm.ChartName == "Playtime per month, last 12 months", "detail: chart of 12 months");

        Check(vm.HideDelayChoices is ["Default (10 s)", "Instantly", "5 s", "10 s", "30 s"] && vm.SelectedHideDelay == "Default (10 s)",
            "detail: hide delay choices, the default selected");
        Check(vm.GameModeChoices is ["Default (off)", "On", "Off"] && vm.SelectedGameMode == "Default (off)", "detail: game mode choices");
        vm.SelectedHideDelay = vm.SelectedHideDelay;
        vm.SelectedHideDelay = null;
        vm.SelectedGameMode = "not a choice";
        vm.AntiCheatReminder = false;
        Check(spy.Edits == 0, "detail: same value or no selection reports nothing");
        vm.SelectedHideDelay = "5 s";
        Check(game.HideDelaySeconds == 5 && spy.Edits == 1 && vm.SelectedHideDelay == "5 s", "detail: hide delay written and saved");
        vm.SelectedGameMode = "Off";
        Check(game.GameMode == false && spy.Edits == 2, "detail: game mode off for this game");

        // Launch options: ticked suggestions are taken out of the extra arguments.
        Check(vm.EngineKnown && vm.EngineTag == "Unity" && vm.Suggestions.Length == 6 && vm.Suggestions[0].IsOn && vm.Suggestions[2].IsOn,
            "detail: Unity suggestions ticked from the arguments");
        Check(vm.ExtraArgs == "-foo" && vm.StartsAs == "Starts as: Steam, with -popupwindow -force-d3d11 -foo",
            "detail: the rest are extra arguments");
        Check(vm.LaunchSummary == "Unity · -popupwindow -force-d3d11 -foo" && vm.CanResetLaunch && vm.HasLaunchNote,
            "detail: summary and the Steam note");
        vm.SetSuggestion(3, true); // DirectX 12: unticks DirectX 11 (same group)
        Check(!vm.Suggestions[2].IsOn && game.LaunchArgs == "-popupwindow -force-d3d12 -foo" && spy.Edits == 3,
            "detail: a suggestion of a group unticks the other, saved");
        vm.ExtraArgs = "-bar";
        Check(game.LaunchArgs == "-popupwindow -force-d3d12 -bar" && spy.Edits == 3, "detail: typing composes without saving");
        vm.CommitExtraArgs();
        Check(spy.Edits == 4, "detail: leaving the field saves");
        vm.SetStartWith(@"C:\Elsewhere\x.exe");
        Check(game.ExeOverride is null && vm.LaunchNote == "Choose a program inside the game's own folder.", "detail: Start with outside the folder refused");
        vm.ResetLaunchCommand.Execute(null);
        Check(game.LaunchArgs is null && vm.ExtraArgs == "" && !vm.CanResetLaunch && vm.Suggestions.All(s => !s.IsOn),
            "detail: Reset clears the options");

        // Anti-cheat: checking, then the scan's findings.
        game.AntiCheats = [AntiCheatKind.EasyAntiCheat];
        vm.Show(game, sessions, settings, crashes);
        Check(newGame == 1 && vm.HasAntiCheat && vm.Checking && vm.AntiCheatState == "Checking…", "detail: same game again; anti-cheat being checked");
        vm.ApplyScan(game, new DetailScan(null, [(PcRequirement.SecureBoot, false), (PcRequirement.Tpm20, true)],
            [new DetailConflict("Cheat Engine", [42])], []));
        Check(vm.AntiCheatIssues == 2 && vm.AntiCheatState == "2 issues" && vm.AntiCheatLabel == "Easy Anti-Cheat · 2 issues",
            "detail: a missing requirement and a running program are issues");
        Check(vm.Requirements[0] is { Status: "Off", Met: false, HelpUrl: not null } && vm.Requirements[1] is { Name: "TPM 2.0", Status: "Found", HelpUrl: null },
            "detail: requirement rows");
        vm.CloseConflict(vm.Conflicts[0].Conflict);

        vm.SetCover(@"C:\pic.png");
        Check(game.CustomCover == @"C:\pic.png" && vm.CoverText == "Custom image" && vm.HasCustomCover, "detail: custom cover");
        vm.DefaultCoverCommand.Execute(null);
        Check(game.CustomCover is null && vm.CoverText == "From Steam", "detail: default cover");

        vm.PlayCommand.Execute(null);
        vm.BackCommand.Execute(null);
        Check(spy.Calls is ["close Cheat Engine", "Play Alpha", "back"], "detail: buttons reach the owner");

        var beta = new Game { Id = "manual:2", Name = "Beta", Source = GameSource.Manual };
        vm.Show(beta, sessions, settings, crashes);
        Check(newGame == 2 && vm.Subtitle == beta.SourceLabel, "detail: another game shown, no launcher name for a manual game");
        Check(!vm.HasSessions && vm.NoSessions && vm.PlaytimeText == "—" && !vm.HasFolder && !vm.HasAntiCheat && !vm.AntiCheatTouched,
            "detail: a game without sessions, folder or anti-cheat");
    }
}
