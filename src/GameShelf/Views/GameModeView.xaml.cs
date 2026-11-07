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
            ToastSwitch.IsOn = settings.ShowRestoreToast;
        }
        finally
        {
            _loading = false;
        }
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
