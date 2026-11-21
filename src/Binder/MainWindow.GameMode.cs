using Binder.Services;
using Binder.Shared;
using Binder.ViewModels;
using Binder.Views;
using Microsoft.UI.Xaml;

namespace Binder;

/// <summary>What the Game mode page asks the panel to do, and what it reads from Windows (<see cref="IGameModeActions"/>).</summary>
public sealed partial class MainWindow
{
    void IGameModeActions.GoBack() => GameModeBack();
    void IGameModeActions.SettingsChanged() => SaveSettings();

    GameModeStatus IGameModeActions.ReadStatus()
    {
        var plan = PowerMode.ActivePlan();
        return new GameModeStatus(
            Allowed: GameModeService.Allowed,
            Outdated: GameModeService.AllowedButOutdated,
            CanAllow: GameModeService.CanAllow,
            PowerNow: GameModeText.Power(PowerMode.Describe()),
            PowerFastest: PowerMode.IsFastest(plan, plan == PowerMode.BalancedPlan ? PowerMode.ActiveMode() : null),
            GameBarRecording: GameModeService.GameBarRecording(),
            VoiceApps: [.. ElevatedPart.InstalledVoiceApps().Select(a => a.Name)],
            Services: ElevatedPart.Services.ToDictionary(s => s.Id, s => GameModeService.ServiceStateOf(s.Id)),
            Windows: new WindowsGamingStatus(
                GameModeOn: WindowsGaming.GameModeOn(),
                HasWindowedOptimizations: WindowsGaming.HasWindowedOptimizations,
                WindowedOptimizationsOn: WindowsGaming.HasWindowedOptimizations && WindowsGaming.WindowedOptimizationsOn(),
                OneDriveRunning: WindowsGaming.OneDriveRunning(),
                DocumentsInOneDrive: WindowsGaming.DocumentsInOneDrive()));
    }

    RestoreStatus IGameModeActions.ReadRestore() =>
        new(GameModeInfo.LastResult(), GameModeService.HasLog, GameModeService.CanRestoreNow, GameModeService.Pending);

    IReadOnlyList<AppChoice> IGameModeActions.RunningApps() =>
        (AppChoice[])[.. GameModeService.RunningApps(_games).Select(a => new AppChoice(a.Path, a.Name))];

    IReadOnlySet<string> IGameModeActions.RunningPaths() => GameModeService.RunningPaths();
    string IGameModeActions.AppName(string path) => GameModeService.AppName(path);
    bool IGameModeActions.AppExists(string path) => File.Exists(path);
    Task<int> IGameModeActions.AllowAsync() => GameModeService.AllowAsync();
    Task IGameModeActions.RemoveAsync() => GameModeService.RemoveAsync();
    bool IGameModeActions.RestoreNow() => GameModeService.RestoreNow();
    void IGameModeActions.OpenLog() => GameModeService.OpenLog();
    void IGameModeActions.PlayRestoredSound() => App.Current.PlayRestoredSound();
    void IGameModeActions.OpenWindowsSettings(string uri) => WindowsGaming.OpenSettings(uri);

    /// <summary>The shelf's "Game mode needs an update" › Update…: the same UAC prompt as Settings › Game mode.</summary>
    private async Task UpdateGameModeHelperAsync()
    {
        App.ModalDepth++; // the UAC prompt takes the focus: click-away must not close the flyout
        int code;
        try
        {
            code = await GameModeService.AllowAsync();
        }
        finally
        {
            App.ModalDepth--;
        }
        if (IsClosing) return;
        RefreshViews(); // closes the notice once the helper is current
        if (code is 0 or 1223) return; // done, or the prompt was declined
        ShowToast(Strings.GmHelperUpdateFailed, Strings.Format(Strings.GmAllowFailed, code, @"ProgramData\Binder\elevated.log"), Strings.Ok);
    }

    // ---- Quick actions (§16): the shelf's "…" menu and the tray menu ---------------------------------------

    /// <summary>
    /// Game mode for all games on or off (games set to On or Off in their own settings keep that), saved at once.
    /// <paramref name="toast"/>: confirm it with an Undo, for the shelf (the tray menu's check mark is enough).
    /// </summary>
    public void SetGameMode(bool on, bool toast = true)
    {
        if (App.Settings.GameModeEnabled == on) return;
        App.Settings.GameModeEnabled = on;
        SaveSettings();
        // Only the header's Game mode button and the helper notice change on the shelf; a full refresh would lay
        // the whole grid out again. The other pages refresh only while shown.
        Shelf.ViewModel!.GameModeChanged(_games);
        RefreshSettingsPage();
        if (Shown(DetailSlot) && Detail.Game is { } shown && FindGame(shown.Id) is { } game)
            Detail.ViewModel!.Show(game, _sessions, App.Settings, App.State.Crashes);
        if (toast)
            ShowToast(on ? Strings.GmIsOn : Strings.GmIsOff,
                on ? Strings.GmOnNextGame : Strings.GmOffNextGame, Strings.Undo,
                () => SetGameMode(!on, toast: false));
    }

    public void OpenGameMode() => ShowGameMode(fromShelf: true);

    private bool _gameModeFromShelf;

    /// <summary>Settings › Game mode (spec §11, board 5), or the same page from the shelf's "…" menu.</summary>
    private void ShowGameMode() => ShowGameMode(fromShelf: false);

    private void ShowGameMode(bool fromShelf)
    {
        _gameModeFromShelf = fromShelf;
        GameModePage.ViewModel!.Show(App.Settings, fromShelf);
        ShowPage(GameModePage);
        GameModePage.UpdateLayout();
        GameModePage.FocusFirst();
    }

    /// <summary>Game mode put Windows back (the toast is showing): update its page if it's open.</summary>
    /// <summary>Game mode was applied (a game started) or put back: the shelf's button and the page's status follow.</summary>
    public void GameModeStateChanged()
    {
        Shelf.ViewModel!.GameModeChanged(_games);
        if (Shown(GameModeSlot)) GameModePage.ViewModel!.UpdateLastRestore();
    }

    private void GameModeBack()
    {
        if (_gameModeFromShelf) ShowShelf();
        else ShowSettings();
    }
}
