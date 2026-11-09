using GameShelf.Library;
using GameShelf.Models;
using GameShelf.Services;
using GameShelf.ViewModels;

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
}
