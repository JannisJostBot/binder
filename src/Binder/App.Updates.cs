using System.Globalization;
using Binder.Library;
using Binder.Models;
using Binder.Services;
using Binder.ViewModels;

namespace Binder;

/// <summary>
/// Updates from GitHub (spec §25): when to check, download and install, and what the panel shows. Lives in App, not
/// the window: a known update and a "Ready to install" outlast the flyout. Nothing runs while the panel is closed:
/// a check starts after a panel's first frame (at most once a day), a download only while a panel is open (closing
/// it cancels), and the install when the panel closes or at sign-in, never while a game or Game mode is active.
/// </summary>
public sealed partial class App
{
    /// <summary>What Settings › About shows; changes raise <see cref="UpdateChanged"/>.</summary>
    public static UpdateStatus CurrentUpdate { get; private set; } = new(UpdatePhase.NotChecked, UpdateManifest.Text(Updater.Installed));

    /// <summary>The panel shows the new state (About card, the "…" dot and item).</summary>
    public event Action? UpdateChanged;

    private static string Installed => UpdateManifest.Text(Updater.Installed);

    private VerifiedRelease? _release;          // the newer verified release known, or null
    private CancellationTokenSource? _checking;  // a check runs
    private CancellationTokenSource? _download;  // a download runs
    private string? _updatedPill;               // "Updated to 0.2.0", shown once at the next panel open

    /// <summary>At start: the update known from an earlier check (verified again), or after <c>--updated</c> the pill.</summary>
    private void InitUpdates(bool updated)
    {
        if (updated)
        {
            var saved = Updater.LoadSaved(newerOnly: false);
            if (saved is { } failed && failed.Manifest.Version > Updater.Installed)
            {
                // The setup ran and the old version is still here: it couldn't install (its log says why).
                _release = failed;
                CurrentUpdate = StatusFor(failed) with { Phase = UpdatePhase.InstallFailed };
                return;
            }
            bool matches = saved is { } s && s.Manifest.Version == Updater.Installed;
            _updatedPill = Strings.Format(matches && saved!.Urgent ? Strings.SecurityUpdated : Strings.Updated, Installed);
            CurrentUpdate = new(UpdatePhase.UpToDate, Installed, Settings.UpdateCheckedAt, Installed, matches ? NotesFor(saved!) : null);
            Updater.EmptyFolder();
            return;
        }
        if (Settings.Updates != UpdateMode.Off && Updater.LoadSaved() is { } known && !IsSkipped(known))
        {
            _release = known;
            CurrentUpdate = StatusFor(known);
            return;
        }
        CurrentUpdate = new(Settings.UpdateCheckedAt is null ? UpdatePhase.NotChecked : UpdatePhase.UpToDate, Installed, Settings.UpdateCheckedAt);
    }

    private static bool IsSkipped(VerifiedRelease release) =>
        !release.Urgent && Settings.UpdateSkipped is { } s && UpdateManifest.ParseVersion(s) == release.Manifest.Version;

    /// <summary>Available or Ready (the setup is in the updates folder), with the notes in the UI's language.</summary>
    private static UpdateStatus StatusFor(VerifiedRelease release)
    {
        var m = release.Manifest;
        return new(Updater.IsDownloaded(m) ? UpdatePhase.Ready : UpdatePhase.Available, Installed, Settings.UpdateCheckedAt,
            UpdateManifest.Text(m.Version), NotesFor(release), 0, m.SetupSize, release.Urgent);
    }

    /// <summary>The notes, with the fixed helper line first when this release brings a newer Game mode helper than the one allowed.</summary>
    private static string[] NotesFor(VerifiedRelease release)
    {
        var notes = release.Manifest.NotesFor(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        if (release.Manifest.HelperVersion is { } helper && GameModeService.AllowedHelperVersion is { } allowed && helper > allowed)
            return [Strings.HelperUpdateNote, .. notes];
        return notes;
    }

    private void SetUpdate(UpdateStatus status)
    {
        if (status == CurrentUpdate) return;
        CurrentUpdate = status;
        UpdateChanged?.Invoke();
    }

    /// <summary>Installs without asking: "Install automatically", or an urgent release (two keys) unless "Don't check".</summary>
    private static bool InstallsByItself(VerifiedRelease release) =>
        Settings.Updates == UpdateMode.Automatic || (release.Urgent && Settings.Updates != UpdateMode.Off);

    /// <summary>
    /// The "…" menu's and the tray menu's first item while an update waits for the user (Ask before installing, not
    /// urgent), else null; it also puts the dot on "…".
    /// </summary>
    public string? UpdateMenuText
    {
        get
        {
            if (_release is not { } r || InstallsByItself(r) || Settings.Updates != UpdateMode.Ask) return null;
            return CurrentUpdate.Phase switch
            {
                UpdatePhase.Ready => Strings.RestartToUpdateMenu,
                UpdatePhase.Available or UpdatePhase.Downloading or UpdatePhase.DownloadFailed or UpdatePhase.NotVerified =>
                    Strings.Format(Strings.UpdateToMenu, UpdateManifest.Text(r.Manifest.Version)),
                _ => null,
            };
        }
    }

    /// <summary>After a panel's first frame: the pill after an update, the daily check, or an automatic download to finish.</summary>
    public void PanelShown()
    {
        if (_updatedPill is { } pill)
        {
            _updatedPill = null;
            _panel?.ShowUpdated(pill, CurrentUpdate.Notes is { Length: > 0 });
        }
        if (Updater.CheckDue(Settings, DateTimeOffset.UtcNow))
        {
            if (!Updater.MustWait()) _ = CheckForUpdatesAsync();
        }
        else if (_release is { } r && InstallsByItself(r) && CurrentUpdate.Phase is UpdatePhase.Available or UpdatePhase.DownloadFailed)
        {
            DownloadUpdate(); // cancelled by an earlier close: start over
        }
    }

    /// <summary>Settings' "Check for updates", or the daily check. One at a time.</summary>
    public async Task CheckForUpdatesAsync()
    {
        if (_checking is not null || _download is not null) return;
        var cancel = _checking = new CancellationTokenSource();
        var before = CurrentUpdate;
        SetUpdate(before with { Phase = UpdatePhase.Checking });
        UpdateCheckResult result;
        try
        {
            result = await Updater.CheckAsync(Settings.UpdateIssued, Settings.UpdateSkipped, cancel.Token);
        }
        catch (OperationCanceledException)
        {
            SetUpdate(before);
            return;
        }
        finally
        {
            _checking = null;
            cancel.Dispose();
        }
        if (_quitting) return;

        var now = DateTimeOffset.UtcNow;
        Settings.UpdateCheckedAt = now;
        if (result.Release is { } verified && (Settings.UpdateIssued is not { } last || verified.Manifest.Issued > last))
            Settings.UpdateIssued = verified.Manifest.Issued;
        SaveSettingsQuietly();

        switch (result.Outcome)
        {
            case UpdateCheckOutcome.Found when result.Release is { } found:
                _release = found;
                SetUpdate(StatusFor(found));
                if (InstallsByItself(found) && CurrentUpdate.Phase == UpdatePhase.Available) DownloadUpdate();
                break;
            case UpdateCheckOutcome.UpToDate:
                _release = null;
                SetUpdate(new(UpdatePhase.UpToDate, Installed, now));
                break;
            case UpdateCheckOutcome.NoConnection:
                SetUpdate(new(UpdatePhase.NoConnection, Installed, now));
                break;
            default:
                SetUpdate(new(UpdatePhase.CheckFailed, Installed, now));
                break;
        }
    }

    /// <summary>"Update" or "Try again", the menus' item, or at once with "Install automatically". One at a time.</summary>
    public async void DownloadUpdate()
    {
        if (_release is not { } release || _download is not null || _checking is not null) return;
        if (Updater.IsDownloaded(release.Manifest))
        {
            SetUpdate(StatusFor(release));
            return;
        }
        var cancel = _download = new CancellationTokenSource();
        SetUpdate(StatusFor(release) with { Phase = UpdatePhase.Downloading });
        long shownMegabyte = 0;
        var progress = new Progress<long>(received =>
        {
            // Reported per chunk; the card changes per megabyte.
            if (!ReferenceEquals(_download, cancel) || received >> 20 == shownMegabyte) return;
            shownMegabyte = received >> 20;
            SetUpdate(CurrentUpdate with { Received = received });
        });
        UpdateDownloadOutcome outcome;
        try
        {
            outcome = await Updater.DownloadAsync(release.Manifest, progress, cancel.Token);
        }
        finally
        {
            if (ReferenceEquals(_download, cancel)) _download = null;
            cancel.Dispose();
        }
        if (_quitting || !ReferenceEquals(_release, release)) return;
        SetUpdate(outcome switch
        {
            UpdateDownloadOutcome.Done or UpdateDownloadOutcome.Cancelled => StatusFor(release),
            UpdateDownloadOutcome.NotVerified => StatusFor(release) with { Phase = UpdatePhase.NotVerified },
            _ => StatusFor(release) with { Phase = UpdatePhase.DownloadFailed },
        });
    }

    /// <summary>The panel is closing: nothing downloads while it's closed.</summary>
    private void CancelUpdateDownload() => _download?.Cancel();

    /// <summary>"Restart now" (or the menus' "Restart to update"): waits for a game or Game mode, else starts the setup and exits.</summary>
    public void InstallUpdate()
    {
        if (_release is not { } release) return;
        if (Updater.MustWait())
        {
            _panel?.ShowNotice(Strings.UpdateWaits, Strings.UpdateWaitsDetail);
            return;
        }
        StartSetup(release);
    }

    private bool StartSetup(VerifiedRelease release)
    {
        switch (Updater.Start(release.Manifest))
        {
            case UpdateStartOutcome.Started:
                Quit(); // the setup's --quit wait then finds nothing to close
                return true;
            case UpdateStartOutcome.NotVerified:
                SetUpdate(StatusFor(release) with { Phase = UpdatePhase.NotVerified });
                return false;
            default:
                SetUpdate(StatusFor(release) with { Phase = UpdatePhase.InstallFailed });
                return false;
        }
    }

    /// <summary>A downloaded update that installs by itself is waiting for the panel to close.</summary>
    private bool InstallDueOnClose =>
        _release is { } r && InstallsByItself(r) && CurrentUpdate.Phase == UpdatePhase.Ready;

    /// <summary>The panel closed, or Windows started us (<c>--background</c>): install now if it's due and nothing is running.</summary>
    private bool TryInstallOnClose()
    {
        if (!InstallDueOnClose || CountdownRunning || Updater.MustWait()) return false;
        return StartSetup(_release!);
    }

    /// <summary>"Skip this version": not offered again (an urgent release still comes back at the next check).</summary>
    public void SkipUpdate()
    {
        if (_release is not { } release) return;
        CancelUpdateDownload();
        Settings.UpdateSkipped = UpdateManifest.Text(release.Manifest.Version);
        SaveSettingsQuietly();
        _release = null;
        Updater.EmptyFolder();
        SetUpdate(new(UpdatePhase.UpToDate, Installed, Settings.UpdateCheckedAt));
    }

    /// <summary>The Updates choice changed: "Don't check" forgets the known update; "Install automatically" fetches it now.</summary>
    public void UpdateModeChanged()
    {
        if (Settings.Updates == UpdateMode.Off)
        {
            CancelUpdateDownload();
            _release = null;
            SetUpdate(new(Settings.UpdateCheckedAt is null ? UpdatePhase.NotChecked : UpdatePhase.UpToDate, Installed, Settings.UpdateCheckedAt));
            return;
        }
        if (_release is { } r && InstallsByItself(r) && CurrentUpdate.Phase == UpdatePhase.Available) DownloadUpdate();
        else UpdateChanged?.Invoke(); // the "…" dot follows the choice
    }

    private static void SaveSettingsQuietly()
    {
        try { Storage.SaveSettings(Settings); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
