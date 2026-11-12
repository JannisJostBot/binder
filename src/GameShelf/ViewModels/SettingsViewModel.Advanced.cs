using GameShelf.Library;
using GameShelf.Models;
using GameShelf.Views;

namespace GameShelf.ViewModels;

/// <summary>One entry of Settings › Advanced: a place a launcher's games or pictures are read from.</summary>
public sealed class SourcePathRow(SourceEntry entry, string label)
{
    public SourceEntry Entry { get; } = entry;
    public string Label { get; } = label;
    /// <summary>The default, shown as the text box's placeholder.</summary>
    public string Placeholder { get; internal set; } = "";
    /// <summary>The user's value as typed (empty: the default).</summary>
    public string Value { get; internal set; } = "";
    /// <summary>Why the typed value wasn't taken, or null.</summary>
    public string? Error { get; internal set; }
    /// <summary>"Binder will download pictures from …" when the download address points elsewhere.</summary>
    public string? Notice { get; internal set; }
    public bool IsChanged => Value.Length > 0;
}

/// <summary>A launcher's entries and its Check result.</summary>
public sealed class SourcePathGroup(GameSource launcher, string name, SourcePathRow[] rows)
{
    public GameSource Launcher { get; } = launcher;
    public string Name { get; } = name;
    public SourcePathRow[] Rows { get; } = rows;
    public string? CheckResult { get; internal set; }
    public bool Checking { get; internal set; }
}

public sealed partial class SettingsViewModel
{
    // --- Advanced: sources and artwork (spec §14) -----------------------------------------------------------

    /// <summary>The launchers' groups, built on <see cref="Load"/>; the view draws them and updates them in place.</summary>
    public SourcePathGroup[] SourceGroups { get; private set; } = [];

    private void LoadSourcePaths()
    {
        var groups = SourceDefaults.All.GroupBy(SourceDefaults.LauncherOf);
        SourceGroups = [.. groups.Select(group => new SourcePathGroup(group.Key, Format.LauncherAppName(group.Key),
            [.. group.Select(entry =>
            {
                var row = new SourcePathRow(entry, EntryLabel(entry)) { Placeholder = SourceDefaults.DefaultText(entry) };
                row.Value = _settings.SourceOverrides.GetValueOrDefault(entry.ToString()) ?? "";
                UpdateNotice(row);
                return row;
            })]))];
    }

    /// <summary>What the entry is, its format and an example: the (i) next to its label.</summary>
    public static string EntryHelp(SourceEntry entry) => entry switch
    {
        SourceEntry.SteamFolder => Strings.HelpSteamFolder,
        SourceEntry.SteamArtFolder => Strings.HelpSteamArtFolder,
        SourceEntry.SteamCoverFiles => Strings.HelpSteamCoverFiles,
        SourceEntry.SteamBannerFile => Strings.HelpSteamBannerFile,
        SourceEntry.SteamLogoFile => Strings.HelpSteamLogoFile,
        SourceEntry.SteamDownload => Strings.HelpSteamDownload,
        SourceEntry.EpicManifests => Strings.HelpEpicManifests,
        SourceEntry.GogKey => Strings.HelpGogKey,
        SourceEntry.UbisoftKey => Strings.HelpUbisoftKey,
        _ => Strings.HelpRockstarKey,
    };

    public static string EntryLabel(SourceEntry entry) => entry switch
    {
        SourceEntry.SteamFolder => Strings.EntrySteamFolder,
        SourceEntry.SteamArtFolder => Strings.EntrySteamArtFolder,
        SourceEntry.SteamCoverFiles => Strings.EntrySteamCoverFiles,
        SourceEntry.SteamBannerFile => Strings.EntrySteamBannerFile,
        SourceEntry.SteamLogoFile => Strings.EntrySteamLogoFile,
        SourceEntry.SteamDownload => Strings.EntrySteamDownload,
        SourceEntry.EpicManifests => Strings.EntryEpicManifests,
        SourceEntry.GogKey => Strings.EntryGamesKey,
        SourceEntry.UbisoftKey => Strings.EntryInstallsKey,
        _ => Strings.EntryLauncherKey,
    };

    private static string ErrorText(SourceValueError error) => error switch
    {
        SourceValueError.Folder => Strings.InvalidFolder,
        SourceValueError.RegistryKey => Strings.InvalidRegistryKey,
        SourceValueError.FileName => Strings.InvalidFileName,
        SourceValueError.FileNames => Strings.InvalidFileNames,
        _ => Strings.InvalidAddress,
    };

    private static void UpdateNotice(SourcePathRow row)
    {
        row.Notice = row.Entry == SourceEntry.SteamDownload && row.Value.Length > 0 && row.Error is null &&
                     SourceDefaults.DownloadHost(row.Value) is { } host && !host.Equals(SourceDefaults.DefaultHost, StringComparison.OrdinalIgnoreCase)
            ? Strings.Format(Strings.DownloadHostNotice, host)
            : null;
    }

    private int ChangedPaths => _settings.SourceOverrides.Count(pair => Enum.TryParse<SourceEntry>(pair.Key, out _));

    /// <summary>The card's second line: only state.</summary>
    public string SourcePathsText => ChangedPaths == 0
        ? Strings.SourcePathsDefault
        : Strings.Plural(ChangedPaths, Strings.SourcePathsChangedOne, Strings.SourcePathsChanged);

    public bool CanResetAllPaths => _settings.SourceOverrides.Count > 0;

    private SourcePathRow? Row(SourceEntry entry) => SourceGroups.SelectMany(g => g.Rows).FirstOrDefault(r => r.Entry == entry);

    /// <summary>
    /// The user finished typing in an entry (Enter or leaving the box). A valid value (or empty: the default) is
    /// stored and used; an invalid one stays in the box with its error and changes nothing.
    /// </summary>
    public void SetSourcePath(SourceEntry entry, string text)
    {
        if (Row(entry) is not { } row) return;
        var error = SourceDefaults.Validate(entry, text, out var normalized);
        if (error != SourceValueError.None)
        {
            row.Value = text.Trim();
            row.Error = ErrorText(error);
            row.Notice = null;
            OnPropertyChanged(nameof(SourceGroups));
            return;
        }
        row.Error = null;
        Store(row, normalized);
    }

    public void ResetSourcePath(SourceEntry entry)
    {
        if (Row(entry) is not { } row) return;
        row.Error = null;
        Store(row, "");
    }

    public void ResetAllSourcePaths()
    {
        bool artwork = false, changed = _settings.SourceOverrides.Count > 0;
        foreach (var row in SourceGroups.SelectMany(g => g.Rows))
        {
            artwork |= row.IsChanged && SourceDefaults.IsArtwork(row.Entry);
            row.Value = "";
            row.Error = row.Notice = null;
        }
        _settings.SourceOverrides.Clear();
        OnPropertyChanged(nameof(SourceGroups));
        if (changed) Changed(artwork ? SettingChange.SourceArtwork : SettingChange.SourcePaths, nameof(SourcePathsText), nameof(CanResetAllPaths));
    }

    private void Store(SourcePathRow row, string value)
    {
        var key = row.Entry.ToString();
        var before = _settings.SourceOverrides.GetValueOrDefault(key) ?? "";
        row.Value = value;
        UpdateNotice(row);
        OnPropertyChanged(nameof(SourceGroups)); // the box shows the tidied value, the error goes away
        if (before == value) return;
        if (value.Length == 0) _settings.SourceOverrides.Remove(key);
        else _settings.SourceOverrides[key] = value;
        Changed(SourceDefaults.IsArtwork(row.Entry) ? SettingChange.SourceArtwork : SettingChange.SourcePaths,
            nameof(SourcePathsText), nameof(CanResetAllPaths));
    }

    /// <summary>"Check": runs that launcher's source once with the values in use and shows what it found.</summary>
    public async Task CheckSourceAsync(GameSource launcher)
    {
        if (SourceGroups.FirstOrDefault(g => g.Launcher == launcher) is not { Checking: false } group) return;
        group.Checking = true;
        group.CheckResult = Strings.Checking;
        OnPropertyChanged(nameof(SourceGroups));
        try
        {
            group.CheckResult = await _actions.CheckSourceAsync(launcher);
        }
        finally
        {
            group.Checking = false;
            OnPropertyChanged(nameof(SourceGroups));
        }
    }
}
