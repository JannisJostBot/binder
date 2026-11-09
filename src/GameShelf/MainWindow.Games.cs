using GameShelf.Library;
using GameShelf.Models;
using GameShelf.Services;
using GameShelf.ViewModels;
using GameShelf.Views;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace GameShelf;

/// <summary>Adding games (desktop shortcuts, a picked or dropped exe) and the actions from a game's menu.</summary>
public sealed partial class MainWindow
{
    /// <summary>Scans the desktops (off the UI thread; reading .lnk files can touch slow drives) and shows the picker.</summary>
    private async Task ShowImportAsync()
    {
        var found = await Task.Run(ShortcutFile.ScanDesktops);
        ImportPage.Show(found, _games);
        ShowPage(ImportPage);
        ImportPage.UpdateLayout();
        ImportPage.FocusFirst();
    }

    /// <summary>Moves the picked shortcuts into our folder and adds the ones that aren't already on the shelf.</summary>
    private void ImportShortcuts(IReadOnlyList<ShortcutFile> picked)
    {
        var result = StoreShortcuts(picked);
        ShowShelf();
        ShowImportResult(result);
    }

    private readonly record struct ImportResult(int Imported, int Copied, int Failed);

    private ImportResult StoreShortcuts(IReadOnlyList<ShortcutFile> picked)
    {
        var records = Storage.LoadImportedShortcuts();
        int imported = 0, copied = 0, failed = 0;
        foreach (var shortcut in picked)
        {
            var existing = ShortcutImporter.ExistingFor(shortcut, _games);
            var record = ShortcutImporter.Store(shortcut, move: true);
            if (record is null)
            {
                failed++;
                continue;
            }
            records.Add(record);
            imported++;
            if (record.Copied) copied++;
            if (existing is not null) continue; // already on the shelf: the shortcut is only tidied away

            var game = ShortcutImporter.CreateGame(shortcut, record.StoredPath, GameSource.Shortcut);
            if (FindGame(game.Id) is { } same) same.ExePath = record.StoredPath;
            else _games.Add(game);
        }

        TrySave(() => Storage.SaveImportedShortcuts(records));
        SaveLibrary();
        RefreshViews();
        ShellNotify.DesktopsChanged();
        return new ImportResult(imported, copied, failed);
    }

    private void ShowImportResult(ImportResult result)
    {
        var (imported, copied, failed) = result;
        var detail = copied > 0
            ? $"{copied} stayed on the shared desktop too: changing it needs administrator rights."
            : "You can put them back from Settings.";
        if (failed > 0) detail += $" {failed} couldn't be imported.";
        ShowToast(imported == 1 ? "Imported 1 shortcut" : $"Imported {imported} shortcuts", detail, "OK");
    }

    /// <summary>Puts every imported shortcut back on the desktop and removes the games made from them.</summary>
    private void RestoreShortcuts()
    {
        var (restored, failed) = ShortcutImporter.RestoreAll(_games);
        SaveLibrary();
        RefreshViews();
        ShowToast(failed == 0 ? "Shortcuts are back on your desktop" : "Some shortcuts couldn't be restored",
            failed == 0 ? $"{restored} restored." : $"{failed} are still in Game Shelf's folder.", "OK");
    }

    /// <summary>"Add a game": pick an .exe or a shortcut with the standard Windows file picker.</summary>
    private void AddGame()
    {
        var path = FilePicker.Open(Root, "Add a game", "Games and shortcuts (*.exe;*.lnk;*.url)|*.exe;*.lnk;*.url",
            keepShortcuts: true); // keep the shortcut itself, with its arguments
        if (path is not null) AddGameFromPath(path, showDetails: true);
    }

    private static bool IsAddable(string path) =>
        path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".url", StringComparison.OrdinalIgnoreCase);

    private static async Task<List<string>> DroppedFilesAsync(DataPackageView data)
    {
        if (!data.Contains(StandardDataFormats.StorageItems)) return [];
        var items = await data.GetStorageItemsAsync();
        return items.OfType<StorageFile>().Select(f => f.Path).Where(p => IsAddable(p) && File.Exists(p)).ToList();
    }

    /// <summary>Drag an .exe or shortcut onto the widget to add it.</summary>
    private async void OnDragOver(object sender, DragEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            e.AcceptedOperation = (await DroppedFilesAsync(e.DataView)).Count > 0 ? DataPackageOperation.Link : DataPackageOperation.None;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        var deferral = e.GetDeferral();
        List<string> addable;
        try { addable = await DroppedFilesAsync(e.DataView); }
        finally { deferral.Complete(); }
        foreach (var file in addable) AddGameFromPath(file, showDetails: addable.Count == 1);
    }

    /// <summary>Adds an .exe or shortcut the user picked or dropped; the user's file stays where it is.</summary>
    private void AddGameFromPath(string path, bool showDetails)
    {
        Game? game;
        if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            game = ShortcutImporter.CreateFromExe(path);
        }
        else if (ShortcutFile.Read(path) is { } shortcut)
        {
            if (ShortcutImporter.ExistingFor(shortcut, _games) is { } existing)
            {
                ShowDetail(existing);
                ShowToast($"{existing.Name} is already on the shelf", "It comes from your " + existing.SourceLabel + " library.", "OK");
                return;
            }
            // Keep the user's file where it is; we launch our own copy.
            var stored = ShortcutImporter.Store(shortcut, move: false);
            game = stored is null ? null : ShortcutImporter.CreateGame(shortcut, stored.StoredPath, GameSource.Manual);
        }
        else
        {
            game = null;
        }
        if (game is null)
        {
            ShowToast("Couldn't add that file", "It isn't a program or a shortcut Game Shelf can read.", "OK");
            return;
        }

        if (FindGame(game.Id) is { } known)
        {
            known.Hidden = false;
            game = known;
        }
        else
        {
            _games.Add(game);
        }
        SaveLibrary();
        RefreshViews();
        if (showDetails) ShowDetail(game);
        else ShowToast($"Added {game.Name}", "It's on the shelf now.", "OK");
    }

    private static void TrySave(Action save)
    {
        try { save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private void Unhide(Game game)
    {
        if (_toastAction is not null) CloseToast(); // a pending "Hid … · Undo" no longer applies
        game.Hidden = false;
        SaveLibrary();
        RefreshViews();
    }

    private void OnGameAction(Game game, GameAction action)
    {
        switch (action)
        {
            case GameAction.Play:
                Play(game);
                break;
            case GameAction.Details:
                ShowDetail(game);
                break;
            case GameAction.OpenFolder:
                if (GameActions.InstallFolder(game) is { } folder) GameActions.ShellOpen(folder);
                break;
            case GameAction.StorePage:
                if (GameActions.StoreUrl(game) is { } url) GameActions.ShellOpen(url);
                break;
            case GameAction.VerifyFiles:
                if (GameActions.SteamCommand(game, "validate") is { } validate) GameActions.ShellOpen(validate);
                break;
            case GameAction.Uninstall:
                if (GameActions.SteamCommand(game, "uninstall") is { } uninstall) GameActions.ShellOpen(uninstall);
                break;
            case GameAction.FreeUpSpace:
                ShowFreeSpace(new FreeSpaceState { Drive = SpacePlanner.DriveOf(game), DrivePicked = true, ReturnGameId = game.Id });
                break;
            case GameAction.ToggleFavorite:
                game.Favorite = !game.Favorite;
                SaveLibrary();
                RefreshViews();
                break;
            case GameAction.Hide:
                HideGame(game);
                break;
            case GameAction.TestLaunch:
                TestLaunch(game);
                break;
        }
    }

    /// <summary>Hides a game from the shelf, with an Undo in the toast.</summary>
    private void HideGame(Game game)
    {
        game.Hidden = true;
        SaveLibrary();
        RefreshViews();

        var id = game.Id;
        ShowToast($"Hid {game.Name}", "It won't show on the shelf anymore.", "Undo", () =>
        {
            if (FindGame(id) is not { } restored) return;
            restored.Hidden = false;
            SaveLibrary();
            RefreshViews();
        });
    }
}
