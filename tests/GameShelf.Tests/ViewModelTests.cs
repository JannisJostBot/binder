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

    /// <summary>Records what the Free up space page asked for; two drives, nothing running.</summary>
    sealed class FreeSpaceActionsSpy : IFreeSpaceActions
    {
        public readonly List<string> Calls = [];
        public readonly HashSet<string> Running = [];
        public DriveSpace[] Drives =
        [
            new(@"C:\", "System", 500 * SpacePlanner.GB, 20 * SpacePlanner.GB),
            new(@"D:\", "Games", 2000 * SpacePlanner.GB, 100 * SpacePlanner.GB),
        ];
        public void GoBack() => Calls.Add("back");
        public void CheckAgain() => Calls.Add("check");
        public IReadOnlyList<DriveSpace> ReadDrives() => Drives;
        public IReadOnlySet<string> RunningGameIds(IReadOnlyList<Game> games) => Running;
        public string UninstallHint(Game game) => "Remove " + game.Name;
        public void OpenUninstall(Game game) => Calls.Add("uninstall " + game.Name);
    }

    static void FreeSpaceViewModelTests()
    {
        var spy = new FreeSpaceActionsSpy();
        var vm = new FreeSpaceViewModel(spy);
        Game G(string id, string dir, long gb, bool favorite = false) =>
            new() { Id = id, Name = id, Source = GameSource.Steam, InstallDir = dir, SizeOnDisk = gb * SpacePlanner.GB, Favorite = favorite };
        var games = new List<Game>
        {
            G("big", @"D:\Games\Big", 150), G("mid", @"D:\Games\Mid", 60), G("small", @"D:\Games\Small", 30),
            G("fav", @"D:\Games\Fav", 200, favorite: true), G("csys", @"C:\Games\Sys", 10), G("nosize", @"D:\Games\X", 0),
            new() { Id = "manual", Name = "manual", Source = GameSource.Manual, InstallDir = @"D:\M", SizeOnDisk = 999 * SpacePlanner.GB },
        };
        var notified = new List<string?>();
        vm.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        var state = new FreeSpaceState { ReturnToShelf = true };
        vm.Show(state, games, []);
        Check(notified is [""] && spy.Calls.Count == 0, "free space: filling the page refreshes once, asks nothing");
        Check(state.Drive == @"D:\" && !state.DrivePicked && vm.SelectedDrive == @"D:\", "free space: the drive with most game data first");
        Check(vm.NeedText == "" && !vm.ShowSummary && vm.Drives.Length == 2 && vm.Drives[1].Text == "100 GB free of 2000 GB",
            "free space: no size yet: no plan, drives without an outcome");
        Check(vm.BackName == "Back to the shelf" && vm.UnknownText.StartsWith("Not counted") && vm.HasUnknown, "free space: back target and unknown sizes");

        vm.SetNeed("150");
        Check(state.Need == 150 * SpacePlanner.GB && vm.ShowSummary && vm.Headline.StartsWith("Frees ") && vm.Picked.Length > 0 &&
              !vm.Picked.Any(g => g.Favorite), "free space: a plan that removes games, favorites kept");
        Check(vm.Drives.Single(d => d.Best).Root == @"D:\" && vm.Drives[0].Text.EndsWith("not enough even after removing games"),
            "free space: best drive marked, the other can't hold it");
        Check(vm.StatusOk && vm.UninstallText.StartsWith("Uninstall ") && vm.CanUninstall, "free space: enough, ready to uninstall");

        var first = vm.Picked[0];
        vm.ToggleGame(first.Game.Id, removedNow: true);
        Check(state.Overrides[first.Game.Id] == false && !vm.Picked.Contains(first), "free space: clicking a suggested tile keeps the game");
        vm.ToggleGame(first.Game.Id, removedNow: false);
        Check(vm.Picked.Contains(first), "free space: clicking it again adds it back");

        int count = vm.Picked.Length;
        vm.UninstallCommand.Execute(null);
        Check(state.Uninstalling && vm.Uninstalling && vm.StepTitle.StartsWith($"1 of {count}: ") && vm.StepMessage.StartsWith("Remove ") &&
              spy.Calls is [var u] && u.StartsWith("uninstall "), "free space: the first game goes to its launcher");
        for (int i = 1; i < count; i++) vm.UninstallCommand.Execute(null);
        Check(vm.UninstallText == "Done", "free space: the last step says Done");
        vm.UninstallCommand.Execute(null);
        Check(!state.Uninstalling && spy.Calls[^1] == "check", "free space: after the last one, check again");

        vm.PickDrive(@"C:\");
        Check(state.DrivePicked && vm.SelectedDrive == @"C:\" && state.Overrides.Count == 0 && vm.CurrentPlan!.Drive.Root == @"C:\",
            "free space: picking a drive starts its plan from scratch");
        vm.SetNeed("5");
        Check(vm.Headline == "It fits already" && vm.Status == "15 GB free afterwards" && !vm.CanUninstall && vm.SelectedDrive == @"C:\",
            "free space: it fits on the picked drive");
        vm.PickDrive(@"Z:\");
        Check(vm.SelectedDrive == @"C:\", "free space: an unknown drive is ignored");

        vm.BackCommand.Execute(null);
        vm.CheckAgainCommand.Execute(null);
        Check(spy.Calls[^2..] is ["back", "check"], "free space: buttons reach the owner");
        Check(FreeSpaceViewModel.Gb(4_500_000_000) == "4.2" && FreeSpaceViewModel.Gb(0) == "0" && FreeSpaceViewModel.Gb(128 * SpacePlanner.GB) == "128",
            "free space: GB rounding");
    }

    /// <summary>Records what the Game mode page asked for; Windows' state is whatever the test sets.</summary>
    sealed class GameModeActionsSpy : IGameModeActions
    {
        public readonly List<string> Calls = [];
        public int Saves;
        public GameModeStatus Status = new(false, false, true, "Balanced", false, true, ["Discord"],
            new Dictionary<string, Shared.ServiceState.Kind> { ["wsearch"] = Shared.ServiceState.Kind.Running });
        public RestoreStatus Restore = new(null, false, false, false);
        public int AllowCode;
        public AppChoice[] Running = [new(@"C:\Apps\Sync\sync.exe", "Sync"), new(@"C:\Apps\Chat\chat.exe", "Chat")];
        public void GoBack() => Calls.Add("back");
        public void SettingsChanged() => Saves++;
        public GameModeStatus ReadStatus() => Status;
        public RestoreStatus ReadRestore() => Restore;
        public IReadOnlyList<AppChoice> RunningApps() => Running;
        public IReadOnlySet<string> RunningPaths() => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\Apps\Chat\chat.exe" };
        public string AppName(string path) => Path.GetFileNameWithoutExtension(path);
        public bool AppExists(string path) => !path.Contains("gone");
        public Task<int> AllowAsync()
        {
            Calls.Add("allow");
            Status = Status with { Allowed = AllowCode == 0 };
            return Task.FromResult(AllowCode);
        }
        public Task RemoveAsync()
        {
            Calls.Add("remove");
            Status = Status with { Allowed = false };
            return Task.CompletedTask;
        }
        public bool RestoreNow()
        {
            Calls.Add("restore");
            return true;
        }
        public void OpenLog() => Calls.Add("log");
        public void PlayRestoredSound() => Calls.Add("sound");
    }

    static void GameModeViewModelTests()
    {
        var spy = new GameModeActionsSpy();
        var vm = new GameModeViewModel(spy);
        var settings = new Settings { GameModeCloseApps = [@"C:\Apps\Chat\chat.exe", @"C:\Apps\gone\old.exe"] };
        var notified = new List<string?>();
        vm.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        vm.Show(settings, fromShelf: true);
        Check(spy.Saves == 0 && spy.Calls.Count == 0, "game mode: filling the page reports no change");
        Check(notified is [""], "game mode: filling the page refreshes every binding once");
        Check(vm.BackName == "Back to the shelf" && vm.BackToolTip == "Back to the shelf (Esc)", "game mode: back says where it goes");
        Check(vm.PowerText == "Now: Balanced. Switched back after the game." && vm.GameBarText.EndsWith("Now: on.") &&
              vm.SmoothText.EndsWith("Always included: the game, Discord."), "game mode: what Windows does now");
        Check(vm.CloseApps.Length == 2 && vm.CloseApps[0].Detail == "Running · reopened after the game" &&
              vm.CloseApps[1].Detail == "Not found: it may have been uninstalled", "game mode: closed apps with their state");
        Check(vm.Services.Length == 4 && vm.Services[0].Detail.EndsWith("Now: running.") && vm.Services[1].Detail.EndsWith("Now: not on this PC."),
            "game mode: services with their state");
        Check(!vm.HelperAllowed && vm.AllowBarOpen && vm.ShowAllow && vm.AllowText == "Allow…" && !vm.AllowWarning,
            "game mode: not allowed yet: the Allow bar");
        Check(vm.LastTitle == "Nothing restored yet" && vm.LastOk is null && vm.RestoreText == "Restore now · nothing pending" && !vm.CanRestore,
            "game mode: nothing restored yet");
        Check(vm.LimitIndex == 1, "game mode: the default upload limit (5 Mbit/s)");

        vm.MasterOn = settings.GameModeEnabled;
        vm.PowerOn = vm.PowerOn;
        vm.LimitIndex = -1;
        vm.LimitIndex = vm.LimitIndex;
        Check(spy.Saves == 0, "game mode: same value or no selection saves nothing");

        vm.MasterOn = true;
        vm.PowerOn = !vm.PowerOn;
        vm.LimitIndex = 2;
        Check(settings.GameModeEnabled && settings.UploadLimitMbps == 20 && spy.Saves == 3, "game mode: switches and the limit write and save");
        bool sysMain = vm.IsItemOn(Shared.GameModeInfo.SysMain);
        vm.SetItem(Shared.GameModeInfo.SysMain, !sysMain);
        Check(settings.GameModeItems.Contains(Shared.GameModeInfo.SysMain) != sysMain && spy.Saves == 4, "game mode: a service row writes its item");
        vm.ToastOn = false;
        Check(!vm.SoundEnabled && !settings.ShowRestoreToast, "game mode: no confirmation, no sound choice");
        vm.SoundOn = false;
        vm.SoundOn = true;
        Check(spy.Calls is ["sound"], "game mode: turning the sound on plays it once");

        Check(vm.AppsToClose() is [{ Name: "Sync" }], "game mode: apps already closed aren't offered");
        vm.AddCloseApp(@"C:\Apps\Sync\sync.exe");
        vm.RemoveCloseApp(@"c:\apps\gone\OLD.exe");
        Check(vm.CloseApps.Select(a => a.Name).SequenceEqual(["chat", "sync"]), "game mode: add and remove closed apps (any case)");
        int uploads = vm.UploadApps.Length;
        vm.AddName(smooth: false, @"C:\Apps\Chat\chat.exe");
        Check(vm.UploadApps.Length == uploads + 1 && vm.UploadApps[^1] == "chat.exe" && !vm.AppsFor(smooth: false).Any(a => a.Name == "Chat"),
            "game mode: uploads list stores the exe name, not offered again");
        vm.RemoveName(smooth: false, "CHAT.EXE");
        Check(vm.UploadApps.Length == uploads, "game mode: removing a name");

        spy.AllowCode = 5;
        vm.AllowAsync().Wait();
        Check(vm.AllowWarning && vm.AllowMessage.Contains("code 5") && vm.AllowEnabled, "game mode: a failed Allow shows its code");
        spy.AllowCode = 0;
        vm.AllowAsync().Wait();
        Check(vm.HelperAllowed && !vm.AllowBarOpen && !vm.AllowWarning, "game mode: allowed, the bar closes");
        vm.RemoveAsync().Wait();
        Check(!vm.HelperAllowed && vm.RemoveEnabled, "game mode: removed again");

        spy.Restore = new(null, true, true, true);
        vm.UpdateLastRestore();
        Check(vm.CanRestore && vm.RestoreText == "Restore now" && vm.HasLog, "game mode: something to restore");
        vm.RestoreCommand.Execute(null);
        Check(!vm.CanRestore && vm.RestoreText == "Restoring…", "game mode: restoring");
        spy.Restore = new((DateTimeOffset.Now, new Shared.RestoreResult(2, 3, ["Spooler"])), true, false, false);
        vm.UpdateLastRestore();
        Check(vm.LastOk == false && vm.LastDetail == "2 of 3 put back · Spooler couldn't be", "game mode: a partial restore");

        vm.LogCommand.Execute(null);
        vm.BackCommand.Execute(null);
        Check(spy.Calls.Skip(1).SequenceEqual(["allow", "allow", "remove", "restore", "log", "back"]), "game mode: buttons reach the owner");
    }
}
