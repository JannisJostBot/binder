using System.Globalization;
using Binder.Models;
using Binder.Views;

namespace Binder.ViewModels;

/// <summary>Where an update stands (spec §25), as Settings › About shows it.</summary>
public enum UpdatePhase
{
    NotChecked, Checking, UpToDate,
    /// <summary>The check couldn't reach GitHub (no connection, timeout, HTTP error).</summary>
    NoConnection,
    /// <summary>The check got something that didn't verify (bad signature, old manifest, …). Logged.</summary>
    CheckFailed,
    Available, Downloading, Ready,
    /// <summary>The download stopped (no connection); "Try again".</summary>
    DownloadFailed,
    /// <summary>The download didn't match the manifest's size and hash, and was deleted; "Try again".</summary>
    NotVerified,
    /// <summary>The setup couldn't be started, or it ran and the old version is still here.</summary>
    InstallFailed,
    /// <summary>A build without release keys (a local build, a fork that set none): never checks.</summary>
    Unavailable,
}

/// <summary>
/// The state the owner hands the About card. <paramref name="Version"/> is the update's (or, after
/// <c>--updated</c>, the installed one whose notes are shown); <paramref name="Notes"/> the lines for What's new.
/// </summary>
public sealed record UpdateStatus(
    UpdatePhase Phase,
    string Installed,
    DateTimeOffset? CheckedAt = null,
    string? Version = null,
    string[]? Notes = null,
    long Received = 0,
    long Total = 0,
    bool Urgent = false);

/// <summary>Settings › About (spec §25, board 28): the version, the update's state and its button, and the Updates choice.</summary>
public sealed partial class SettingsViewModel
{
    // The order of the Updates combo's items in SettingsView.xaml.
    private static readonly UpdateMode[] UpdateModes = [UpdateMode.Automatic, UpdateMode.Ask, UpdateMode.Off];

    private UpdateStatus _update = new(UpdatePhase.NotChecked, "");

    public RelayCommand CheckUpdatesCommand { get; }
    /// <summary>The accent button: Update, Try again or Restart now, by <see cref="UpdatePhase"/>.</summary>
    public RelayCommand UpdateActionCommand { get; }
    public RelayCommand SkipUpdateCommand { get; }

    private static readonly string[] AboutNames =
    [
        nameof(AboutTitle), nameof(AboutText), nameof(AboutToolTip), nameof(ShowCheckButton), nameof(CanCheckUpdates),
        nameof(UpdateActionText), nameof(ShowUpdateAction), nameof(ShowDownloadProgress), nameof(DownloadPercent),
        nameof(HasNotes), nameof(NotesText), nameof(CanSkipUpdate), nameof(UpdateStatus), nameof(ShowUpdatesChoice),
    ];

    /// <summary>The owner reports a new state (a check, a download's progress, a result). Refreshes only the About card.</summary>
    public void SetUpdate(UpdateStatus status)
    {
        if (status == _update) return;
        _update = status;
        foreach (var name in AboutNames) OnPropertyChanged(name);
    }

    public UpdateStatus UpdateStatus => _update;

    public string AboutTitle => Strings.Format(Strings.AboutTitle, _update.Installed);

    /// <summary>The card's second line: only the state ("Up to date · checked today at 14:02", "0.2.0 is available").</summary>
    public string AboutText => _update.Phase switch
    {
        UpdatePhase.Checking => Strings.UpdateChecking,
        UpdatePhase.UpToDate when _update.CheckedAt is { } at => CheckedText(at),
        UpdatePhase.UpToDate or UpdatePhase.NotChecked => _settings.Updates == UpdateMode.Off ? Strings.UpdateCheckOff : Strings.UpdateNotChecked,
        UpdatePhase.NoConnection => Strings.UpdateNoConnection,
        UpdatePhase.CheckFailed => Strings.UpdateCheckFailed,
        UpdatePhase.Available => Strings.Format(Strings.UpdateAvailable, _update.Version),
        UpdatePhase.Downloading => Strings.Format(Strings.UpdateDownloading, Megabytes(_update.Received), Megabytes(_update.Total)),
        UpdatePhase.Ready => Strings.UpdateReady,
        UpdatePhase.DownloadFailed => Strings.UpdateDownloadFailed,
        UpdatePhase.NotVerified => Strings.UpdateNotVerified,
        UpdatePhase.InstallFailed => Strings.UpdateInstallFailed,
        UpdatePhase.Unavailable => Strings.UpdatesUnavailable,
        _ => "",
    };

    /// <summary>Where the setup's log is, for "couldn't be installed"; why a build doesn't check; nothing otherwise.</summary>
    public string? AboutToolTip => _update.Phase switch
    {
        UpdatePhase.InstallFailed => Strings.Format(Strings.UpdateInstallFailedToolTip, Path.Combine(Path.GetTempPath(), "BinderSetup.log")),
        UpdatePhase.Unavailable => Strings.UpdatesUnavailableToolTip,
        _ => null,
    };

    private static string CheckedText(DateTimeOffset at)
    {
        var local = at.ToLocalTime();
        return local.Date == DateTime.Today
            ? Strings.Format(Strings.UpdateCheckedToday, local.ToString("t", CultureInfo.CurrentCulture))
            : Strings.Format(Strings.UpdateChecked, Strings.LowerFirst(Format.RelativeDay(at)));
    }

    private static int Megabytes(long bytes) => (int)Math.Ceiling(bytes / (1024.0 * 1024.0));

    /// <summary>"Check for updates" shows when there's nothing to do with an update.</summary>
    public bool ShowCheckButton => !ShowUpdateAction && _update.Phase is not (UpdatePhase.Downloading or UpdatePhase.Unavailable);

    /// <summary>The Updates choice, hidden in a build that never checks.</summary>
    public bool ShowUpdatesChoice => _update.Phase != UpdatePhase.Unavailable;
    public bool CanCheckUpdates => _update.Phase != UpdatePhase.Checking;

    public string UpdateActionText => _update.Phase switch
    {
        UpdatePhase.Available => Strings.UpdateButton,
        UpdatePhase.DownloadFailed or UpdatePhase.NotVerified => Strings.TryAgain,
        UpdatePhase.Ready => Strings.RestartNow,
        _ => "",
    };

    public bool ShowUpdateAction => UpdateActionText.Length > 0;
    public bool ShowDownloadProgress => _update.Phase == UpdatePhase.Downloading;
    public double DownloadPercent => _update.Total <= 0 ? 0 : Math.Clamp(100.0 * _update.Received / _update.Total, 0, 100);

    private void UpdateAction()
    {
        switch (_update.Phase)
        {
            case UpdatePhase.Available or UpdatePhase.DownloadFailed or UpdatePhase.NotVerified:
                _actions.DownloadUpdate();
                break;
            case UpdatePhase.Ready:
                _actions.InstallUpdate();
                break;
        }
    }

    /// <summary>What's new opens from the card while there are notes (a known update, or the one just installed).</summary>
    public bool HasNotes => _update.Notes is { Length: > 0 };

    /// <summary>One line per note, as plain text (never markup or links).</summary>
    public string NotesText => _update.Notes is { } notes ? string.Join(Environment.NewLine, notes.Select(n => "• " + n)) : "";

    /// <summary>A normal update can be skipped; an urgent one can't (it would come back at the next check anyway).</summary>
    public bool CanSkipUpdate => !_update.Urgent && _update.Phase is UpdatePhase.Available or UpdatePhase.DownloadFailed or UpdatePhase.NotVerified;

    public int UpdateModeIndex
    {
        get => IndexOf(UpdateModes, _settings.Updates);
        set
        {
            if (TryPick(UpdateModes, value, out var mode))
                Set(_settings.Updates, mode, v => _settings.Updates = v, SettingChange.Updates, [nameof(AboutText)], nameof(UpdateModeIndex));
        }
    }
}
