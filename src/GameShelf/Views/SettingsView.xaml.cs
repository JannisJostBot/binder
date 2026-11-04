using System.Globalization;
using GameShelf.Models;
using GameShelf.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using DockPosition = GameShelf.Models.DockPosition;

namespace GameShelf.Views;

/// <summary>What a settings change affects, so the window applies only that part.</summary>
public enum SettingChange { Behavior, DisplayMode, Hotkey, TrayIcon, Shelf, Sources, Position, Screen, DesktopLayer, Autostart }

/// <summary>
/// App settings, shown in place of the grid like the detail view. Changes apply immediately; the owner
/// saves and applies them through <see cref="Changed"/>.
/// </summary>
public sealed partial class SettingsView : UserControl
{
    public event Action? BackRequested;
    public event Action<SettingChange>? Changed;
    public event Action? RefreshRequested;
    public event Action<Game>? UnhideRequested;
    public event Action? ImportRequested;
    public event Action? RestoreRequested;
    public event Action? AddGameRequested;
    public event Action? SetupRequested;

    private Settings _settings = new();
    private bool _loading; // true while controls are filled from the settings, so their events are ignored

    public SettingsView()
    {
        InitializeComponent();
        foreach (var hotkey in Enum.GetValues<Hotkey>())
            HotkeyBox.Items.Add(new ComboBoxItem { Content = App.HotkeyLabel(hotkey), Tag = hotkey.ToString() });
    }

    /// <summary>Fills the controls. <paramref name="autostartOn"/> comes from the registry, the source of truth.</summary>
    public void Show(Settings settings, IReadOnlyList<Game> games, bool autostartOn, bool refreshing, int importedShortcuts,
        IReadOnlyList<MonitorInfo> monitors, string? currentMonitor, bool hotkeyFree)
    {
        _loading = true;
        try
        {
            _settings = settings;
            ModeChoice.SelectedIndex = settings.DisplayMode == DisplayMode.Desktop ? 1 : 0;
            Select(HotkeyBox, settings.Hotkey.ToString());
            HotkeyStatus.Text = settings.Hotkey == Hotkey.Off ? "Off"
                : hotkeyFree ? "Free, not used by Windows or other apps"
                : "Taken by another app: choose a different one";
            TraySwitch.IsOn = settings.TrayIcon;
            StayReadySwitch.IsOn = settings.StayReady;
            Select(KeepLoadedBox, settings.KeepPanelLoaded ? "Fast" : "Light");
            UpdateKeepLoaded();
            ClickAwaySwitch.IsOn = settings.CloseOnClickAway;

            Select(DelayBox, settings.HideDelaySeconds.ToString(CultureInfo.InvariantCulture));
            ComeBackSwitch.IsOn = settings.ComeBackOnClose;
        WeekSwitch.IsOn = settings.ShowWeeklySummary;
            Select(DensityBox, settings.GridDensity.ToString());
            SteamCheck.IsChecked = settings.SteamEnabled;
            EpicCheck.IsChecked = settings.EpicEnabled;
            GogCheck.IsChecked = settings.GogEnabled;
            EaCheck.IsChecked = settings.EaEnabled;
            UbisoftCheck.IsChecked = settings.UbisoftEnabled;
            XboxCheck.IsChecked = settings.XboxEnabled;
            RockstarCheck.IsChecked = settings.RockstarEnabled;
            ShortcutsCheck.IsChecked = settings.ShortcutsEnabled;
            ShortcutsText.Text = importedShortcuts switch
            {
                0 => "Move game shortcuts off your desktop into the shelf",
                1 => "1 shortcut imported",
                _ => $"{importedShortcuts} shortcuts imported",
            };
            RestoreButton.IsEnabled = importedShortcuts > 0;

            Select(PositionBox, settings.Position.ToString());
            PositionText.Text = settings.Position == DockPosition.Free
                ? "Drag the widget by an empty spot to move it"
                : "Docked to the edge of the screen";

            FillScreens(monitors, currentMonitor);
            DesktopSwitch.IsOn = settings.StayOnDesktop;
            AutostartSwitch.IsOn = autostartOn;
            AutostartText.Text = autostartOn ? "Opens when you sign in" : "Off";

            FillHidden(games);

            int shown = games.Count(g => !g.Hidden && settings.IsSourceEnabled(g.Source));
            LibraryText.Text = refreshing ? "Scanning your stores…" : $"{shown} games on the shelf";
            RefreshButton.IsEnabled = !refreshing;
        }
        finally
        {
            _loading = false;
        }
    }

    public void FocusFirst() => BackButton.Focus(FocusState.Programmatic);

    private static void Select(ComboBox box, string tag) =>
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == tag);

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;

    /// <summary>One entry per monitor, left to right; only shown with more than one monitor.</summary>
    private void FillScreens(IReadOnlyList<MonitorInfo> monitors, string? current)
    {
        ScreenRow.Visibility = monitors.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        ScreenBox.Items.Clear();
        if (monitors.Count < 2) return;

        int number = 1;
        foreach (var monitor in monitors.OrderBy(m => m.Bounds.Left).ThenBy(m => m.Bounds.Top))
        {
            var label = monitor.IsPrimary ? $"{number} (main)" : number.ToString(CultureInfo.CurrentCulture);
            var item = new ComboBoxItem { Content = $"{label} · {monitor.Bounds.Width} × {monitor.Bounds.Height}", Tag = monitor.Device };
            ScreenBox.Items.Add(item);
            if (string.Equals(monitor.Device, current, StringComparison.OrdinalIgnoreCase)) ScreenBox.SelectedItem = item;
            number++;
        }
    }

    private void FillHidden(IReadOnlyList<Game> games)
    {
        var hidden = games.Where(g => g.Hidden)
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        HiddenList.Children.Clear();
        foreach (var game in hidden)
        {
            if (HiddenList.Children.Count > 0) HiddenList.Children.Add(new Border { Style = (Style)Application.Current.Resources["RowDivider"] });
            var row = new Grid { Style = (Style)Application.Current.Resources["SettingRow"] };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel { Style = (Style)Application.Current.Resources["RowText"] };
            text.Children.Add(new TextBlock { Text = game.Name, Style = (Style)Application.Current.Resources["RowTitle"], TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock { Text = game.SourceLabel, Style = (Style)Application.Current.Resources["RowDetail"] });
            row.Children.Add(text);
            var show = new Button { Content = "Show", VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(show, "Show " + game.Name);
            show.Click += (_, _) => UnhideRequested?.Invoke(game);
            Grid.SetColumn(show, 1);
            row.Children.Add(show);
            HiddenList.Children.Add(row);
        }
        NoHiddenText.Visibility = hidden.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Change(SettingChange change)
    {
        if (!_loading) Changed?.Invoke(change);
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => BackRequested?.Invoke();

    private void OnModeChanged(object sender, SelectionChangedEventArgs e)
    {
        // RadioButtons reports a selection made while filling the page later, after _loading is over: only a
        // real change counts (it rebuilds the window).
        if (_loading || ModeChoice.SelectedItem is not RadioButton { Tag: string tag }) return;
        var mode = Enum.Parse<DisplayMode>(tag);
        if (mode == _settings.DisplayMode) return;
        _settings.DisplayMode = mode;
        Change(SettingChange.DisplayMode);
    }

    private void OnHotkeyChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || SelectedTag(HotkeyBox) is not { } tag) return;
        _settings.Hotkey = Enum.Parse<Hotkey>(tag);
        Change(SettingChange.Hotkey);
    }

    private void OnTrayChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.TrayIcon = TraySwitch.IsOn;
        Change(SettingChange.TrayIcon);
    }

    private void OnFlyoutOptionChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.StayReady = StayReadySwitch.IsOn;
        _settings.KeepPanelLoaded = SelectedTag(KeepLoadedBox) == "Fast";
        _settings.CloseOnClickAway = ClickAwaySwitch.IsOn;
        UpdateKeepLoaded();
        Change(SettingChange.Behavior);
    }

    /// <summary>The trade-off in words; the choice only matters while it stays ready in the background.</summary>
    private void UpdateKeepLoaded()
    {
        KeepLoadedBox.IsEnabled = _settings.StayReady;
        KeepLoadedDetail.Text = !_settings.StayReady
            ? "Only while it stays ready in the background"
            : _settings.KeepPanelLoaded
                ? "Opens at once; stays loaded (up to ~200 MB, which Windows reclaims first). Games aren't affected: it exits while you play."
                : "Frees all but a few MB, so it opens a moment slower. Games aren't affected: it exits while you play.";
    }

    private void OnSetupClick(object sender, RoutedEventArgs e) => SetupRequested?.Invoke();

    private void OnDelayChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || SelectedTag(DelayBox) is not { } tag) return;
        _settings.HideDelaySeconds = int.Parse(tag, CultureInfo.InvariantCulture);
        Change(SettingChange.Behavior);
    }

    private void OnDensityChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || SelectedTag(DensityBox) is not { } tag) return;
        _settings.GridDensity = Enum.Parse<GridDensity>(tag);
        Change(SettingChange.Shelf);
    }

    private void OnWeekChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.ShowWeeklySummary = WeekSwitch.IsOn;
        Change(SettingChange.Shelf);
    }

    private void OnComeBackChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.ComeBackOnClose = ComeBackSwitch.IsOn;
        Change(SettingChange.Behavior);
    }

    private void OnSourceChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.SteamEnabled = SteamCheck.IsChecked == true;
        _settings.EpicEnabled = EpicCheck.IsChecked == true;
        _settings.GogEnabled = GogCheck.IsChecked == true;
        _settings.EaEnabled = EaCheck.IsChecked == true;
        _settings.UbisoftEnabled = UbisoftCheck.IsChecked == true;
        _settings.XboxEnabled = XboxCheck.IsChecked == true;
        _settings.RockstarEnabled = RockstarCheck.IsChecked == true;
        _settings.ShortcutsEnabled = ShortcutsCheck.IsChecked == true;
        Change(SettingChange.Sources);
    }

    private void OnImportClick(object sender, RoutedEventArgs e) => ImportRequested?.Invoke();

    private void OnRestoreClick(object sender, RoutedEventArgs e) => RestoreRequested?.Invoke();

    private void OnAddGameClick(object sender, RoutedEventArgs e) => AddGameRequested?.Invoke();

    private void OnRefreshClick(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke();

    private void OnPositionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || SelectedTag(PositionBox) is not { } tag) return;
        _settings.Position = Enum.Parse<DockPosition>(tag);
        Change(SettingChange.Position);
    }

    private void OnScreenChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || SelectedTag(ScreenBox) is not { } device) return;
        _settings.Monitor = device;
        Change(SettingChange.Screen);
    }

    private void OnDesktopChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.StayOnDesktop = DesktopSwitch.IsOn;
        Change(SettingChange.DesktopLayer);
    }

    private void OnAutostartChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.StartWithWindows = AutostartSwitch.IsOn;
        Change(SettingChange.Autostart);
    }
}
