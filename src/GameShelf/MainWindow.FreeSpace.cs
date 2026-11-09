using GameShelf.Library;
using GameShelf.Models;
using GameShelf.Services;
using GameShelf.ViewModels;
using Microsoft.UI.Xaml;

namespace GameShelf;

/// <summary>What the Free up space page asks the panel to do, and what it reads from Windows (<see cref="IFreeSpaceActions"/>).</summary>
public sealed partial class MainWindow
{
    void IFreeSpaceActions.GoBack() => LeaveFreeSpace();
    void IFreeSpaceActions.CheckAgain() => _ = CheckFreeSpaceAgainAsync();

    IReadOnlyList<DriveSpace> IFreeSpaceActions.ReadDrives()
    {
        var drives = new List<DriveSpace>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                var label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Local Disk" : drive.VolumeLabel;
                drives.Add(new DriveSpace(drive.RootDirectory.FullName.ToUpperInvariant(), label, drive.TotalSize, drive.AvailableFreeSpace));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return [.. drives.OrderBy(d => d.Root, StringComparer.OrdinalIgnoreCase)];
    }

    IReadOnlySet<string> IFreeSpaceActions.RunningGameIds(IReadOnlyList<Game> games) => RunningGames.Ids(games);
    string IFreeSpaceActions.UninstallHint(Game game) => GameActions.Uninstall(game).Hint;
    void IFreeSpaceActions.OpenUninstall(Game game) => GameActions.ShellOpen(GameActions.Uninstall(game).Target);

    /// <summary>
    /// Free up space (spec §12), from Settings or a game's "…" menu (that game's drive selected). Mid-uninstall
    /// a reopened panel comes back here with the same plan and fresh free-space numbers.
    /// </summary>
    private void ShowFreeSpace(FreeSpaceState state)
    {
        App.State.FreeSpace = state;
        FreeSpacePage.ViewModel!.Show(state, _games, _sessions);
        ShowPage(FreeSpacePage);
        FreeSpacePage.UpdateLayout();
        FreeSpacePage.FocusFirst();
    }

    /// <summary>Free up space as from Settings (or where an uninstall left it), with Back to the shelf.</summary>
    public void OpenFreeSpace()
    {
        var state = App.State.FreeSpace is { Uninstalling: true } current ? current : new FreeSpaceState();
        state.ReturnGameId = null;
        state.ReturnToShelf = true;
        ShowFreeSpace(state);
    }

    private void LeaveFreeSpace()
    {
        var returnTo = App.State.FreeSpace?.ReturnGameId is { } id ? FindGame(id) : null;
        if (returnTo is not null && !returnTo.Hidden) ShowDetail(returnTo);
        else if (App.State.FreeSpace?.ReturnToShelf == true) ShowShelf();
        else ShowSettings();
    }

    /// <summary>"Check again", and after the last game was handed to its launcher: rescan, then re-plan.</summary>
    private async Task CheckFreeSpaceAgainAsync()
    {
        await RescanAsync();
        if (FreeSpacePage.Visibility == Visibility.Visible && App.State.FreeSpace is { } state)
            FreeSpacePage.ViewModel!.Show(state, _games, _sessions);
    }
}
