using GameShelf.Services;
using GameShelf.Shared;
using GameShelf.ViewModels;

namespace GameShelf;

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
            PowerNow: PowerMode.Describe(),
            PowerFastest: PowerMode.IsFastest(plan, plan == PowerMode.BalancedPlan ? PowerMode.ActiveMode() : null),
            GameBarRecording: GameModeService.GameBarRecording(),
            VoiceApps: [.. ElevatedPart.InstalledVoiceApps().Select(a => a.Name)],
            Services: ElevatedPart.Services.ToDictionary(s => s.Id, s => GameModeService.ServiceStateOf(s.Id)));
    }

    RestoreStatus IGameModeActions.ReadRestore() =>
        new(GameModeInfo.LastResult(), GameModeService.HasLog, GameModeService.CanRestoreNow, GameModeService.Pending);

    IReadOnlyList<AppChoice> IGameModeActions.RunningApps() =>
        [.. GameModeService.RunningApps(_games).Select(a => new AppChoice(a.Path, a.Name))];

    IReadOnlySet<string> IGameModeActions.RunningPaths() => GameModeService.RunningPaths();
    string IGameModeActions.AppName(string path) => GameModeService.AppName(path);
    bool IGameModeActions.AppExists(string path) => File.Exists(path);
    Task<int> IGameModeActions.AllowAsync() => GameModeService.AllowAsync();
    Task IGameModeActions.RemoveAsync() => GameModeService.RemoveAsync();
    bool IGameModeActions.RestoreNow() => GameModeService.RestoreNow();
    void IGameModeActions.OpenLog() => GameModeService.OpenLog();
    void IGameModeActions.PlayRestoredSound() => App.Current.PlayRestoredSound();
}
