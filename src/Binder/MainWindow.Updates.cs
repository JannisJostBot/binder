using Binder.Library;
using Binder.Services;
using Binder.ViewModels;

namespace Binder;

/// <summary>Updates from GitHub in the panel (spec §25): About's buttons, the "…" item and dot, the pill after an update.</summary>
public sealed partial class MainWindow
{
    void ISettingsActions.CheckForUpdates() => _ = App.Current.CheckForUpdatesAsync();
    void ISettingsActions.DownloadUpdate() => App.Current.DownloadUpdate();
    void ISettingsActions.InstallUpdate() => App.Current.InstallUpdate();
    void ISettingsActions.SkipUpdate() => App.Current.SkipUpdate();
    void ISettingsActions.OpenRepository()
    {
        if (UpdateManifest.Repository.Length > 0) GameActions.ShellOpen("https://github.com/" + UpdateManifest.Repository);
    }
    void IShelfActions.StartUpdate() => StartUpdateFromMenu();

    /// <summary>Connected in the constructor; the window stops listening when it closes.</summary>
    private void ListenForUpdates()
    {
        App.Current.UpdateChanged += PushUpdate;
        Closed += (_, _) => App.Current.UpdateChanged -= PushUpdate;
        PushUpdate();
    }

    /// <summary>The About card and the "…" dot and item follow the app's update state.</summary>
    private void PushUpdate()
    {
        SettingsPage.ViewModel!.SetUpdate(App.CurrentUpdate);
        Shelf.ViewModel!.SetUpdate(App.Current.UpdateMenuText);
    }

    /// <summary>
    /// "Update to 0.2.0" / "Restart to update" (the "…" menu or the tray): installs a downloaded update, else starts
    /// the download and shows it in Settings › About.
    /// </summary>
    public void StartUpdateFromMenu()
    {
        if (App.CurrentUpdate.Phase == UpdatePhase.Ready)
        {
            App.Current.InstallUpdate();
            return;
        }
        App.Current.DownloadUpdate();
        ShowSettings();
        SettingsPage.ShowAbout(openNotes: false);
    }

    /// <summary>Once after an update: "Updated to 0.2.0" with What's new, which opens Settings › About with the notes.</summary>
    public void ShowUpdated(string title, bool hasNotes)
    {
        if (hasNotes) ShowToast(title, "", Strings.WhatsNew, () =>
        {
            ShowSettings();
            SettingsPage.ShowAbout(openNotes: true);
        });
        else ShowToast(title, "", Strings.Ok);
    }
}
