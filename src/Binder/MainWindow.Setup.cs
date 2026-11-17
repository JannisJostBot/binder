using Binder.Library;
using Binder.Models;
using Binder.Services;
using Binder.ViewModels;
using Microsoft.UI.Xaml;

namespace Binder;

/// <summary>What first-run setup asks the panel to do (<see cref="ISetupActions"/>).</summary>
public sealed partial class MainWindow
{
    IReadOnlyList<GameSource> ISetupActions.Launchers => LibraryScanner.StoreSources;
    void ISetupActions.Skip() => FinishSetup(null);
    void ISetupActions.LoadShortcuts() => _ = LoadSetupShortcutsAsync();
    void ISetupActions.Finish(SetupChoices choices) => FinishSetup(choices);

    void ISetupActions.Import(IReadOnlyList<ShortcutFile> picked)
    {
        // Shown in step 3 itself: the panel's notice would sit over its Done button. First the picked shortcuts fly
        // into Binder's logo (skipped with animations off), then step 3. The moves run in the background (with many
        // shortcuts they take a moment), so the animation starts at once; the games go onto the shelf as soon as
        // they're moved, still during the animation (it runs on the compositor), so step 3 follows it straight away
        // and says what actually went.
        var moving = Task.Run(() => MoveShortcuts(picked));
        var adding = AddWhenMoved(moving);
        SetupPage.PlayMerge(picked, async () =>
        {
            var (title, detail) = ImportResultText(await adding);
            SetupPage.ViewModel!.ImportDone(title, detail);
        });
    }

    private async Task<ImportResult> AddWhenMoved(Task<List<(ShortcutFile Shortcut, ImportedShortcut? Record)>> moving) =>
        AddStored(await moving); // back on the UI thread

    /// <summary>First-run setup (board 10). Also reachable from Settings → "Run first-time setup again".</summary>
    private void ShowSetup()
    {
        _sourcesBeforeSetup = LibraryScanner.StoreSources.Where(App.Settings.IsSourceEnabled).ToHashSet();
        SetupPage.ViewModel!.Start(App.Settings, _games, Autostart.IsEnabled());
        ShowPage(SetupPage);
        _ = CountLaunchersForSetupAsync();
    }

    private HashSet<GameSource> _sourcesBeforeSetup = [];

    /// <summary>Setup lists every launcher with its games, also the ones turned off, so it reads each one once.</summary>
    private async Task CountLaunchersForSetupAsync()
    {
        Dictionary<GameSource, int> counts;
        try { counts = await Task.Run(LibraryScanner.CountAll); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }
        if (!IsClosing && Shown(SetupSlot)) SetupPage.ViewModel!.SetFoundCounts(counts);
    }

    private async Task LoadSetupShortcutsAsync()
    {
        var found = await Task.Run(ShortcutFile.ScanDesktops);
        SetupPage.ViewModel!.SetShortcuts(found, _games);
    }

    /// <summary>Applies the setup's answers. <paramref name="choices"/> is null for "Skip setup" (keep the defaults).</summary>
    private void FinishSetup(SetupChoices? choices)
    {
        var settings = App.Settings;
        bool modeChanged = false;
        if (choices is not null)
        {
            modeChanged = settings.DisplayMode != choices.Mode;
            settings.DisplayMode = choices.Mode;
            if (choices.StartWithWindows != Autostart.IsEnabled() && !Autostart.TrySet(choices.StartWithWindows))
                ShowToast(Strings.AutostartFailed, Strings.AutostartFailedDetail, Strings.Ok);
            settings.StartWithWindows = Autostart.IsEnabled();
        }
        settings.SetupDone = true;
        SaveSettings();
        bool newLauncher = LibraryScanner.StoreSources.Any(s => settings.IsSourceEnabled(s) && !_sourcesBeforeSetup.Contains(s));
        if (newLauncher) App.State.Scanned = false; // the next window (after a mode change) scans again too
        if (modeChanged)
        {
            App.Current.ApplyDisplayMode(); // this window closes; the new one opens in the chosen mode
            return;
        }
        RefreshViews(); // launchers may have been unticked in step 1
        ShowShelf();
        Shelf.FocusSearch();
        // A launcher ticked on in step 1 wasn't scanned: its games come in now.
        if (newLauncher) _ = RescanAsync();
    }
}
