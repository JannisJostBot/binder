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
            var record = ShortcutImporter.Store(shortcut, move: true, records); // recorded and saved before it moves
            if (record is null)
            {
                failed++;
                continue;
            }
            imported++;
            if (record.Copied) copied++;
            if (existing is not null) continue; // already on the shelf: the shortcut is only tidied away

            var game = ShortcutImporter.CreateGame(shortcut, record.StoredPath, GameSource.Shortcut);
            if (FindGame(game.Id) is { } same) same.ExePath = record.StoredPath;
            else AddToLibrary(game);
        }

        SaveLibrary();
        RefreshViews();
        ShellNotify.DesktopsChanged();
        return new ImportResult(imported, copied, failed);
    }

    private void ShowImportResult(ImportResult result)
    {
        var (imported, copied, failed) = result;
        var detail = copied > 0
            ? Strings.Plural(copied, Strings.ImportCopiedOne, Strings.ImportCopied)
            : Strings.ImportRestoreHint;
        if (failed > 0) detail += " " + Strings.Plural(failed, Strings.ImportFailedOne, Strings.ImportFailed);
        ShowToast(Strings.Plural(imported, Strings.ImportDoneOne, Strings.ImportDone), detail, Strings.Ok);
    }

    /// <summary>Puts every imported shortcut back on the desktop and removes the games made from them.</summary>
    private void RestoreShortcuts()
    {
        var (restored, failed) = ShortcutImporter.RestoreAll(_games);
        SaveLibrary();
        RefreshViews();
        ShowToast(failed == 0 ? Strings.RestoredShortcuts : Strings.RestoreShortcutsFailed,
            failed == 0 ? Strings.Plural(restored, Strings.RestoredCountOne, Strings.RestoredCount) : Strings.Plural(failed, Strings.StillInFolderOne, Strings.StillInFolder), Strings.Ok);
    }

    /// <summary>"Add a game": pick an .exe or a shortcut with the standard Windows file picker.</summary>
    private void AddGame()
    {
        var path = FilePicker.Open(Root, Strings.AddGame, Strings.Format(Strings.FilterGames, "*.exe;*.lnk;*.url") + "|*.exe;*.lnk;*.url",
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
                ShowToast(Strings.Format(Strings.AlreadyOnShelfTitle, existing.Name), Strings.Format(Strings.FromLibrary, existing.SourceLabel), Strings.Ok);
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
            ShowToast(Strings.CouldntAddFile, Strings.CouldntAddFileDetail, Strings.Ok);
            return;
        }

        if (FindGame(game.Id) is { } known)
        {
            known.Hidden = false;
            game = known;
        }
        else
        {
            AddToLibrary(game);
        }
        SaveLibrary();
        RefreshViews();
        if (showDetails) ShowDetail(game);
        else ShowToast(Strings.Format(Strings.AddedGame, game.Name), Strings.AddedGameDetail, Strings.Ok);
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

    /// <summary>Adds a new game; one that was on the shelf before gets its kept settings back.</summary>
    private void AddToLibrary(Game game)
    {
        var removed = new List<Game>(App.State.RemovedGames);
        if (RemovedGames.Restore(removed, game))
        {
            App.State.SetRemovedGames(removed);
            TrySave(() => Storage.SaveRemovedGames(removed));
            game.Hidden = false; // the user just added it
        }
        _games.Add(game);
    }

    /// <summary>
    /// Play and Details check the game is still installed (one attribute read). An uninstalled or deleted game
    /// leaves the shelf at once, with a toast, instead of waiting for the next rescan. Its settings are kept.
    /// </summary>
    private bool RemoveIfGone(Game game)
    {
        if (!GameFiles.IsGone(game)) return false;
        _games.RemoveAll(g => g.Id == game.Id);
        ForgetGames([game], new List<Game>(App.State.RemovedGames));
        SaveLibrary();
        RefreshViews(); // back to the shelf if its details were open
        ShowToast(Strings.Format(Strings.GameGoneTitle, game.Name), Strings.GameGoneDetail, Strings.Ok);
        return true;
    }

    /// <summary>Hides a game from the shelf, with an Undo in the toast.</summary>
    private void HideGame(Game game)
    {
        game.Hidden = true;
        SaveLibrary();
        RefreshViews();

        var id = game.Id;
        ShowToast(Strings.Format(Strings.HidGame, game.Name), Strings.HidGameDetail, Strings.Undo, () =>
        {
            if (FindGame(id) is not { } restored) return;
            restored.Hidden = false;
            SaveLibrary();
            RefreshViews();
        });
    }
}
