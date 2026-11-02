using GameShelf.Library;
using GameShelf.Models;
using GameShelf.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GameShelf.Views;

/// <summary>One launcher in setup step 1.</summary>
public sealed class SetupLauncherRow(GameSource source, string name, string count, bool enabled)
{
    public GameSource Source { get; } = source;
    public string Name { get; } = name;
    public string Count { get; } = count;
    public bool Enabled { get; set; } = enabled;
}

/// <summary>The setup's answers, applied by the window.</summary>
public sealed record SetupChoices(DisplayMode Mode, bool StartWithWindows);

/// <summary>
/// First-run setup, three steps (spec §6, board 10): which launchers, tidy the desktop (the shortcut picker,
/// embedded), how it opens. Shown once; Settings can run it again.
/// </summary>
public sealed partial class SetupView : UserControl
{
    public event Action? Skipped;
    /// <summary>Step 2 needs the desktop's shortcuts (scanned by the window, off the UI thread).</summary>
    public event Action? ShortcutsNeeded;
    public event Action<IReadOnlyList<ShortcutFile>>? ImportRequested;
    public event Action<SetupChoices>? Finished;

    private Settings _settings = new();
    private List<SetupLauncherRow> _launchers = [];
    private bool _shortcutsLoaded;

    public SetupView()
    {
        InitializeComponent();
        Picker.UseEmbedded();
        Picker.SelectionChanged += UpdateMoveButton;
    }

    /// <summary>Starts at step 1 with the library as found so far (the scan may still be running).</summary>
    public void Start(Settings settings, IReadOnlyList<Game> games, bool startWithWindows)
    {
        _settings = settings;
        _shortcutsLoaded = false;
        UpdateLibrary(games);
        FlyoutCard.IsChecked = settings.DisplayMode == DisplayMode.Flyout;
        DesktopCard.IsChecked = settings.DisplayMode == DisplayMode.Desktop;
        AutostartSwitch.IsOn = startWithWindows;
        HotkeyText.Text = App.HotkeyLabel(settings.Hotkey);
        ShowStep(1);
    }

    /// <summary>Launchers with their game counts. Called again when the scan finishes.</summary>
    public void UpdateLibrary(IReadOnlyList<Game> games)
    {
        var counts = games.Where(g => LauncherIcons.HasBadge(g.Source))
            .GroupBy(g => g.Source)
            .OrderByDescending(group => group.Count())
            .ToList();
        _launchers = counts.Select(group => new SetupLauncherRow(group.Key, LauncherBadge.Label(group.Key),
            group.Count() == 1 ? "1 game" : $"{group.Count()} games", _settings.IsSourceEnabled(group.Key))).ToList();

        LauncherList.Children.Clear();
        foreach (var row in _launchers)
        {
            var line = new Grid();
            var box = new CheckBox { Content = row.Name, IsChecked = row.Enabled, Margin = new Thickness(0, 4, 0, 4) };
            box.Checked += (_, _) => row.Enabled = true;
            box.Unchecked += (_, _) => row.Enabled = false;
            line.Children.Add(box);
            line.Children.Add(new TextBlock
            {
                Text = row.Count,
                Style = (Style)Application.Current.Resources["RowDetail"],
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0),
            });
            LauncherList.Children.Add(line);
        }
        NothingFoundText.Visibility = _launchers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        int total = counts.Sum(group => group.Count());
        FoundTitle.Text = total == 0 ? "Let's set up your shelf" : total == 1 ? "We found 1 game" : $"We found {total} games";
    }

    public void SetShortcuts(IReadOnlyList<ShortcutFile> shortcuts, IReadOnlyList<Game> library)
    {
        _shortcutsLoaded = true;
        Picker.Show(shortcuts, library);
        UpdateMoveButton();
    }

    /// <summary>After the picked shortcuts were moved: on to the last step.</summary>
    public void ImportDone() => ShowStep(3);

    private void ShowStep(int step)
    {
        Step1.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        Step3.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        if (step == 2 && !_shortcutsLoaded) ShortcutsNeeded?.Invoke();
    }

    private void UpdateMoveButton()
    {
        int selected = Picker.Selected.Count;
        MoveButton.Content = selected == 0 ? "Move" : $"Move {selected}";
        MoveButton.IsEnabled = selected > 0;
        SelectionText.Text = selected == 0 ? "Nothing selected" : selected == 1 ? "1 selected" : $"{selected} selected";
    }

    private void OnSkipClick(object sender, RoutedEventArgs e) => Skipped?.Invoke();

    private void OnStep1Next(object sender, RoutedEventArgs e)
    {
        foreach (var row in _launchers)
        {
            switch (row.Source)
            {
                case GameSource.Steam: _settings.SteamEnabled = row.Enabled; break;
                case GameSource.Epic: _settings.EpicEnabled = row.Enabled; break;
                case GameSource.Gog: _settings.GogEnabled = row.Enabled; break;
                case GameSource.Ea: _settings.EaEnabled = row.Enabled; break;
                case GameSource.Ubisoft: _settings.UbisoftEnabled = row.Enabled; break;
            }
        }
        ShowStep(2);
    }

    private void OnBackToStep1(object sender, RoutedEventArgs e) => ShowStep(1);

    private void OnBackToStep2(object sender, RoutedEventArgs e) => ShowStep(2);

    private void OnStep2Skip(object sender, RoutedEventArgs e) => ShowStep(3);

    private void OnStep2Move(object sender, RoutedEventArgs e) => ImportRequested?.Invoke(Picker.Selected);

    private void OnDone(object sender, RoutedEventArgs e) =>
        Finished?.Invoke(new SetupChoices(DesktopCard.IsChecked == true ? DisplayMode.Desktop : DisplayMode.Flyout,
            AutostartSwitch.IsOn));
}
