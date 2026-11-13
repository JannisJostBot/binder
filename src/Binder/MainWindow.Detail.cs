using Binder.Library;
using Binder.Models;
using Binder.Services;
using Binder.ViewModels;
using Binder.Views;

namespace Binder;

/// <summary>What the detail page asks the panel to do, and what it needs to know from Windows (<see cref="IDetailActions"/>).</summary>
public sealed partial class MainWindow
{
    void IDetailActions.GoBack() => ShowShelf();
    void IDetailActions.Act(Game game, GameAction action) => OnGameAction(game, action);

    void IDetailActions.GameEdited(Game game)
    {
        SaveLibrary();
        Shelf.ViewModel!.SetData(_games, _sessions, App.Settings); // e.g. a new cover shows on the tile too
    }

    void IDetailActions.CrashSeen(CrashRecord record) => CrashStore.MarkSeen(App.State.Crashes, record);
    void IDetailActions.CloseConflict(DetailConflict conflict) => PcChecks.AskToClose(new PcChecks.Conflict(conflict.Name, conflict.ProcessIds));
    void IDetailActions.OpenLink(string url) => GameActions.ShellOpen(url);

    string? IDetailActions.InstallFolder(Game game) => GameActions.InstallFolder(game);
    bool IDetailActions.HasStorePage(Game game) => GameActions.StoreUrl(game) is not null;
    string IDetailActions.DescribeStart(Game game) => Launcher.Describe(game);

    bool IDetailActions.ImportCover(Game game, string path)
    {
        try
        {
            game.CustomCover = CoverCache.ImportCustomCover(game, path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    void IDetailActions.ClearCover(Game game) => CoverCache.ClearCustomCover(game);

    /// <summary>The engine and anti-cheat (from the install folder, cached on the game), then this PC's checks and running programs.</summary>
    async Task<DetailScan?> IDetailActions.ScanAsync(Game game)
    {
        try
        {
            return await Task.Run(() =>
            {
                var result = GameScan.Get(game);
                IReadOnlyList<AntiCheatKind> kinds = result?.AntiCheats ?? game.AntiCheats ?? [];
                var requirements = AntiCheat.Requirements(kinds, game.Name, PcChecks.IsWindows11)
                    .Select(r => (r, PcChecks.Meets(r)))
                    .ToArray();
                bool any = kinds.Count > 0;
                return new DetailScan(result, requirements,
                    any ? [.. PcChecks.RunningConflicts().Select(c => new DetailConflict(c.Name, c.ProcessIds))] : [],
                    any ? [.. PcChecks.BlockedDriverTools()] : []);
            });
        }
        catch (Exception e)
        {
            ErrorLog.Write(e, "game scan");
            return null;
        }
    }
}
