using GameShelf.Models;
using GameShelf.Services;
using GameShelf.Shared;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace GameShelf.Views;

/// <summary>
/// Settings › Game mode (spec §11, board 5). The current state (power mode, Game Bar, which apps run, the last
/// restore) is read once when the page opens or after a restore, never watched.
/// </summary>
public sealed partial class GameModeView : UserControl
{
    public event Action? BackRequested;
    /// <summary>A setting changed: save settings.json.</summary>
    public event Action? Changed;

    private Settings _settings = new();
    private IReadOnlyList<Game> _games = [];
    private bool _loading;

    public GameModeView()
    {
        InitializeComponent();
    }

    public void Show(Settings settings, IReadOnlyList<Game> games)
    {
        _settings = settings;
        _games = games;
        _loading = true;
        try
        {
            MasterSwitch.IsOn = settings.GameModeEnabled;
            PowerSwitch.IsOn = settings.GameModeItems.Contains(GameModeInfo.Power);
            PrioritySwitch.IsOn = settings.GameModeItems.Contains(GameModeInfo.Priority);
            GameBarSwitch.IsOn = settings.GameModeItems.Contains(GameModeInfo.GameBar);
            SmoothSwitch.IsOn = settings.GameModeItems.Contains(GameModeInfo.Smooth);
            UploadsSwitch.IsOn = settings.GameModeItems.Contains(GameModeInfo.Uploads);
            UpdatesSwitch.IsOn = settings.GameModeItems.Contains(GameModeInfo.Updates);
            ToastSwitch.IsOn = settings.ShowRestoreToast;
            SoundSwitch.IsOn = settings.RestoreSound;
            SoundSwitch.IsEnabled = settings.ShowRestoreToast;
            int limit = Array.IndexOf(ElevatedPart.UploadLimits, settings.UploadLimitMbps);
            LimitBox.SelectedIndex = limit >= 0 ? limit : Array.IndexOf(ElevatedPart.UploadLimits, ElevatedPart.DefaultUploadLimit);
        }
        finally
        {
            _loading = false;
        }
        UpdatesText.Text = $"Windows Update uses at most {ElevatedPart.UpdatePercent}% of the bandwidth for background downloads.";
        UpdateAdminPart();
        var plan = PowerMode.ActivePlan();
        PowerText.Text = PowerMode.IsFastest(plan, plan == PowerMode.BalancedPlan ? PowerMode.ActiveMode() : null)
            ? $"Now: {PowerMode.Describe()}, already the fastest. Left as is."
            : $"Now: {PowerMode.Describe()}. Switched back after the game.";
        GameBarText.Text = "Turns off “Record what happened” while you play. Now: " +
                           (GameModeService.GameBarRecording() ? "on." : "off.");
        FillApps();
        UpdateLastRestore();
    }

    public void FocusFirst() => BackButton.Focus(FocusState.Programmatic);

    /// <summary>Back goes to the shelf (opened from its "…" menu) or to Settings; the button says which.</summary>
    public void SetBackTarget(bool shelf)
    {
        var back = shelf ? "Back to the shelf" : "Back to settings";
        ToolTipService.SetToolTip(BackButton, back + " (Esc)");
        AutomationProperties.SetName(BackButton, back);
    }

    /// <summary>A restore just finished (the toast is up): show its result and whether anything is still pending.</summary>
    public void UpdateLastRestore()
    {
        if (GameModeInfo.LastResult() is { } last)
        {
            LastTitle.Text = "Last restore: " + Format.SessionStart(last.When);
            LastDetail.Text = last.Result.Success
                ? $"{last.Result.Restored} of {last.Result.Total} {(last.Result.Total == 1 ? "change" : "changes")} put back"
                : $"{last.Result.Restored} of {last.Result.Total} put back · {string.Join(", ", last.Result.Failed)} couldn't be";
            LastIcon.Glyph = last.Result.Success ? "" : "";
            LastIcon.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
                last.Result.Success ? "SystemFillColorSuccessBrush" : "SystemFillColorCautionBrush"];
        }
        else
        {
            LastTitle.Text = "Nothing restored yet";
            LastDetail.Text = "Game mode's changes are put back when the game closes.";
            LastIcon.Glyph = ""; // info
            LastIcon.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        }
        LogButton.IsEnabled = GameModeService.HasLog;

        bool canRestore = GameModeService.CanRestoreNow;
        RestoreButton.IsEnabled = canRestore;
        RestoreButton.Content = canRestore ? "Restore now"
            : GameModeService.Pending ? "Restore now · a game is running"
            : "Restore now · nothing pending";
    }

    private void FillApps()
    {
        AppList.Children.Clear();
        var running = _settings.GameModeCloseApps.Count > 0 ? GameModeService.RunningPaths() : [];
        foreach (var path in _settings.GameModeCloseApps)
        {
            if (AppList.Children.Count > 0) AppList.Children.Add(new Border { Style = Resource<Style>("RowDivider") });
            var name = GameModeService.AppName(path);
            var row = new Grid { Style = Resource<Style>("SettingRow") };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel { Style = Resource<Style>("RowText") };
            text.Children.Add(new TextBlock { Text = name, Style = Resource<Style>("RowTitle"), TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock
            {
                Text = !File.Exists(path) ? "Not found: it may have been uninstalled"
                    : running.Contains(path) ? "Running · reopened after the game"
                    : "Not running now",
                Style = Resource<Style>("RowDetail"),
            });
            ToolTipService.SetToolTip(text, path);
            row.Children.Add(text);
            var remove = new Button { Content = "", Style = Resource<Style>("IconButton"), VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(remove, "Remove " + name);
            ToolTipService.SetToolTip(remove, "Remove");
            remove.Click += (_, _) =>
            {
                _settings.GameModeCloseApps.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
                Changed?.Invoke();
                FillApps();
                AddAppButton.Focus(FocusState.Programmatic);
            };
            Grid.SetColumn(remove, 1);
            row.Children.Add(remove);
            AppList.Children.Add(row);
        }
    }

    // ---- The admin part ------------------------------------------------------------------------------------

    /// <summary>The banner, the services' rows (with their state now), and what needs the helper enabled or not.</summary>
    private void UpdateAdminPart()
    {
        bool allowed = GameModeService.Allowed;
        bool outdated = allowed && GameModeService.AllowedButOutdated;
        bool canAllow = GameModeService.CanAllow;
        AllowBar.Severity = InfoBarSeverity.Informational;
        AllowBar.IsOpen = !allowed || outdated;
        AllowBar.Message = outdated
            ? "The helper is from another version of Game Shelf. Update it to keep these working."
            : canAllow
                ? "Pausing services and network priority use a small helper. Windows asks you once to allow it; it can only do what's listed here."
                : "Only an administrator account can allow this. The other items work without it.";
        AllowButton.Content = outdated ? "Update…" : "Allow…";
        AllowButton.Visibility = canAllow ? Visibility.Visible : Visibility.Collapsed;
        AllowButton.IsEnabled = true;
        RemovePanel.Visibility = allowed ? Visibility.Visible : Visibility.Collapsed;

        FillServices(allowed);
        foreach (var control in new Control[] { SmoothSwitch, UploadsSwitch, UpdatesSwitch, LimitBox, AddSmoothButton, AddUploadButton })
            control.IsEnabled = allowed;
        var voice = ElevatedPart.InstalledVoiceApps().Select(a => a.Name).ToList();
        SmoothText.Text = "Marks their traffic as urgent (DSCP 46); helps only if your router honors it. Always included: the game" +
                          (voice.Count > 0 ? ", " + string.Join(", ", voice) : "") + ".";
        FillNames(SmoothList, _settings.KeepSmoothApps, AddSmoothButton, allowed);
        FillNames(UploadList, _settings.ThrottledUploadApps, AddUploadButton, allowed);
    }

    private static readonly Dictionary<string, string> ServiceDetails = new()
    {
        [GameModeInfo.WindowsSearch] = "Stops indexing files; search still works.",
        [GameModeInfo.SysMain] = "Stops preloading apps into memory.",
        [GameModeInfo.Telemetry] = "Stops sending diagnostic data to Microsoft.",
        [GameModeInfo.PrintSpooler] = "Printing waits until the game closes.",
    };

    private void FillServices(bool allowed)
    {
        ServiceList.Children.Clear();
        foreach (var (id, _, name) in ElevatedPart.Services)
        {
            if (ServiceList.Children.Count > 0) ServiceList.Children.Add(new Border { Style = Resource<Style>("RowDivider") });
            var state = GameModeService.ServiceStateOf(id);
            var row = new Grid { Style = Resource<Style>("SettingRow") };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel { Style = Resource<Style>("RowText") };
            text.Children.Add(new TextBlock { Text = name, Style = Resource<Style>("RowTitle") });
            text.Children.Add(new TextBlock
            {
                Text = ServiceDetails[id] + " Now: " + state switch
                {
                    ServiceState.Kind.Running => "running.",
                    ServiceState.Kind.Stopped => "stopped, left as is.",
                    ServiceState.Kind.Disabled => "turned off, left as is.",
                    _ => "not on this PC.",
                },
                Style = Resource<Style>("RowDetail"),
                TextWrapping = TextWrapping.Wrap,
            });
            row.Children.Add(text);
            var toggle = new ToggleSwitch
            {
                Style = Resource<Style>("RowSwitch"),
                Tag = id,
                IsOn = _settings.GameModeItems.Contains(id),
                IsEnabled = allowed,
            };
            AutomationProperties.SetName(toggle, "Pause " + name);
            toggle.Toggled += OnItemToggled;
            Grid.SetColumn(toggle, 1);
            row.Children.Add(toggle);
            ServiceList.Children.Add(row);
        }
    }

    /// <summary>A list of exe names with a remove button each (kept smooth, or slowed down).</summary>
    private void FillNames(StackPanel list, List<string> names, Button addButton, bool enabled)
    {
        list.Children.Clear();
        foreach (var exe in names)
        {
            var row = new Grid { Padding = new Thickness(0, 2, 0, 2), ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock
            {
                Text = Path.GetFileNameWithoutExtension(exe),
                Style = Resource<Style>("RowTitle"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            var remove = new Button
            {
                Content = "", // Cancel (×), as on the close-apps rows
                Style = Resource<Style>("IconButton"),
                VerticalAlignment = VerticalAlignment.Center,
                IsEnabled = enabled,
            };
            AutomationProperties.SetName(remove, "Remove " + Path.GetFileNameWithoutExtension(exe));
            ToolTipService.SetToolTip(remove, "Remove");
            remove.Click += (_, _) =>
            {
                names.RemoveAll(n => string.Equals(n, exe, StringComparison.OrdinalIgnoreCase));
                Changed?.Invoke();
                FillNames(list, names, addButton, enabled);
                addButton.Focus(FocusState.Programmatic);
            };
            Grid.SetColumn(remove, 1);
            row.Children.Add(remove);
            list.Children.Add(row);
        }
    }

    private void OnAddSmoothClick(object sender, RoutedEventArgs e) =>
        ShowAppMenu(AddSmoothButton, _settings.KeepSmoothApps, SmoothList);

    private void OnAddUploadClick(object sender, RoutedEventArgs e) =>
        ShowAppMenu(AddUploadButton, _settings.ThrottledUploadApps, UploadList);

    /// <summary>Running apps with a window (read on click); the pick is stored by exe name, as QoS matches it.</summary>
    private void ShowAppMenu(Button button, List<string> names, StackPanel list)
    {
        var menu = new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };
        var apps = GameModeService.RunningApps(_games)
            .Where(a => ElevatedPart.IsAppName(Path.GetFileName(a.Path)) &&
                        !names.Contains(Path.GetFileName(a.Path), StringComparer.OrdinalIgnoreCase))
            .ToList();
        foreach (var app in apps)
        {
            var item = new MenuFlyoutItem { Text = app.Name };
            ToolTipService.SetToolTip(item, app.Path);
            item.Click += (_, _) =>
            {
                names.Add(Path.GetFileName(app.Path));
                Changed?.Invoke();
                FillNames(list, names, button, true);
            };
            menu.Items.Add(item);
        }
        if (apps.Count == 0) menu.Items.Add(new MenuFlyoutItem { Text = "No other apps with a window are open", IsEnabled = false });
        App.ModalDepth++;
        menu.Closed += (_, _) => App.ModalDepth--;
        menu.ShowAt(button);
    }

    private void OnLimitChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LimitBox.SelectedItem is not ComboBoxItem { Tag: string tag } || !int.TryParse(tag, out int limit)) return;
        _settings.UploadLimitMbps = limit;
        Changed?.Invoke();
    }

    private async void OnAllowClick(object sender, RoutedEventArgs e)
    {
        AllowButton.IsEnabled = false;
        App.ModalDepth++; // the UAC prompt takes the focus: click-away must not close the flyout
        int code;
        try
        {
            code = await GameModeService.AllowAsync();
        }
        finally
        {
            App.ModalDepth--;
        }
        UpdateAdminPart();
        if (code is 0 or 1223) return; // done, or the prompt was declined
        AllowBar.Severity = InfoBarSeverity.Warning;
        AllowBar.Message = $"That didn't work (code {code}). The helper's log is in ProgramData\\GameShelf\\elevated.log.";
    }

    private async void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        RemoveButton.IsEnabled = false;
        App.ModalDepth++;
        try
        {
            await GameModeService.RemoveAsync();
        }
        finally
        {
            App.ModalDepth--;
            RemoveButton.IsEnabled = true;
        }
        AllowBar.Severity = InfoBarSeverity.Informational;
        UpdateAdminPart();
    }

    private static T Resource<T>(string key) => (T)Application.Current.Resources[key];

    private void OnBackClick(object sender, RoutedEventArgs e) => BackRequested?.Invoke();

    private void OnMasterToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.GameModeEnabled = MasterSwitch.IsOn;
        Changed?.Invoke();
    }

    private void OnItemToggled(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not ToggleSwitch { Tag: string id } toggle) return;
        _settings.GameModeItems.Remove(id);
        if (toggle.IsOn) _settings.GameModeItems.Add(id);
        Changed?.Invoke();
    }

    private void OnToastToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.ShowRestoreToast = ToastSwitch.IsOn;
        SoundSwitch.IsEnabled = ToastSwitch.IsOn; // the sound comes with the confirmation
        Changed?.Invoke();
    }

    private void OnSoundToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.RestoreSound = SoundSwitch.IsOn;
        if (SoundSwitch.IsOn) App.Current.PlayRestoredSound(); // so the user hears what they turned on
        Changed?.Invoke();
    }

    /// <summary>Lists the apps with a window right now (read on click) to pick one from.</summary>
    private void OnAddAppClick(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };
        var apps = GameModeService.RunningApps(_games)
            .Where(a => !_settings.GameModeCloseApps.Contains(a.Path, StringComparer.OrdinalIgnoreCase))
            .ToList();
        foreach (var app in apps)
        {
            var item = new MenuFlyoutItem { Text = app.Name };
            ToolTipService.SetToolTip(item, app.Path);
            item.Click += (_, _) =>
            {
                _settings.GameModeCloseApps.Add(app.Path);
                Changed?.Invoke();
                FillApps();
            };
            menu.Items.Add(item);
        }
        if (apps.Count == 0) menu.Items.Add(new MenuFlyoutItem { Text = "No other apps with a window are open", IsEnabled = false });
        App.ModalDepth++; // click-away must not close the flyout under the menu
        menu.Closed += (_, _) => App.ModalDepth--;
        menu.ShowAt(AddAppButton);
    }

    private void OnLogClick(object sender, RoutedEventArgs e) => GameModeService.OpenLog();

    private void OnRestoreClick(object sender, RoutedEventArgs e)
    {
        if (!GameModeService.RestoreNow()) return;
        RestoreButton.IsEnabled = false;
        RestoreButton.Content = "Restoring…"; // the result comes back with the toast (UpdateLastRestore)
    }
}
