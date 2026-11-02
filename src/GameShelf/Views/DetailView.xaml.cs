using GameShelf.Library;
using GameShelf.Models;
using GameShelf.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace GameShelf.Views;

/// <summary>
/// Details for one game, shown in place of the grid inside the same panel. Everything here is computed once
/// when the view opens; nothing updates in the background.
/// </summary>
public sealed partial class DetailView : UserControl
{
    private const int RecentSessionCount = 5;
    private static readonly int[] DelayChoices = [0, 5, 10, 30];

    public event Action? BackRequested;
    public event Action<Game, GameAction>? ActionRequested;
    /// <summary>A per-game setting changed; the owner saves the library.</summary>
    public event Action<Game>? GameEdited;

    private Game? _game;
    private Settings _settings = new();
    private bool _loading;

    public DetailView()
    {
        InitializeComponent();
    }

    /// <summary>The game being shown, or null.</summary>
    public Game? Game => _game;

    public void Show(Game game, IReadOnlyList<Session> sessions, Settings settings)
    {
        bool sameGame = ReferenceEquals(_game, game) || _game?.Id == game.Id;
        _game = game;
        _settings = settings;

        SetArt(Banner, game);
        SetArt(Cover, game);
        TitleText.Text = game.Name;
        ToolTipService.SetToolTip(TitleText, game.Name);
        SubtitleText.Text = LauncherIcons.HasBadge(game.Source) ? LauncherBadge.Label(game.Source) : game.SourceLabel;
        AutomationProperties.SetName(PlayButton, "Play " + game.Name);
        UpdateFavorite();

        bool hasFolder = GameActions.InstallFolder(game) is not null;
        FolderButton.IsEnabled = hasFolder;
        ToolTipService.SetToolTip(FolderButton, hasFolder ? "Open install folder" : "Install folder not found");
        StoreLink.Visibility = GameActions.StoreUrl(game) is null ? Visibility.Collapsed : Visibility.Visible;

        ShowStats(game, sessions);
        UpdateSettingsRows();
        if (!sameGame) _scan = null;
        UpdateLaunchOptions();
        UpdateAntiCheat();
        _ = ScanAsync(game);
        if (!sameGame) Scroller.ChangeView(null, 0, null, disableAnimation: true);
    }

    public void FocusPlay() => PlayButton.Focus(FocusState.Programmatic);

    private static void SetArt(CoverView view, Game game)
    {
        // Same instance: force a reload (the art may have changed); the decoded image usually comes from memory.
        if (ReferenceEquals(view.Game, game)) view.Refresh();
        else view.Game = game;
    }

    private void ShowStats(Game game, IReadOnlyList<Session> sessions)
    {
        var mine = sessions
            .Where(s => s.GameId == game.Id && s.End > s.Start)
            .OrderByDescending(s => s.End)
            .ToList();

        var total = TimeSpan.FromTicks(mine.Sum(s => (s.End - s.Start).Ticks));
        PlaytimeText.Text = mine.Count == 0 ? "—" : Format.Playtime(total);
        LastPlayedText.Text = mine.Count == 0 ? "Never" : Format.RelativeDay(mine[0].End);
        SizeText.Text = game.SizeOnDisk > 0 ? game.SizeText : "—";

        SessionsList.Children.Clear();
        foreach (var session in mine.Take(RecentSessionCount))
        {
            if (SessionsList.Children.Count > 0) SessionsList.Children.Add(new Border { Style = (Style)Application.Current.Resources["RowDivider"] });
            var row = new Grid { MinHeight = 40 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = Format.SessionStart(session.Start), VerticalAlignment = VerticalAlignment.Center });
            var length = new TextBlock
            {
                Text = Format.Duration(session.End - session.Start),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            };
            Grid.SetColumn(length, 1);
            row.Children.Add(length);
            SessionsList.Children.Add(row);
        }
        NoSessionsText.Visibility = mine.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        _mine = mine;
        PlaytimeSection.Visibility = mine.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (mine.Count > 0) ShowPlaytimeChart();
    }

private List<Session> _mine = [];

    /// <summary>Hours per week for 8 weeks, or per month for 12, with this period's total above the bars.</summary>
    private void ShowPlaytimeChart()
    {
        bool months = MonthsItem.IsSelected;
        var now = DateTime.Now;
        var buckets = months ? PlaytimeStats.Months(_mine, now) : PlaytimeStats.Weeks(_mine, now);
        var period = TimeSpan.FromTicks(buckets.Sum(b => b.Time.Ticks));
        var current = buckets[^1].Time;

        PeriodTotalText.Text = period > TimeSpan.Zero ? Format.Duration(period) : "Not played";
        PeriodDetailText.Text = (months ? "in the last 12 months" : "in the last 8 weeks") +
            $" · {(months ? "this month" : "this week")} {(current > TimeSpan.Zero ? Format.Duration(current) : "none yet")}";
        PlaytimeChart.Show(
            buckets.Select(b => new Bar(
                months ? PlaytimeStats.MonthLabel(b.Start) : PlaytimeStats.WeekLabel(b.Start),
                (months ? PlaytimeStats.MonthName(b.Start) : "Week of " + PlaytimeStats.WeekLabel(b.Start)) + ": " +
                    (b.Time > TimeSpan.Zero ? Format.Duration(b.Time) : "not played"),
                b.Time.TotalMinutes)).ToList(),
            months ? "Playtime per month, last 12 months" : "Playtime per week, last 8 weeks");
    }

    private void OnPeriodChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_mine.Count > 0) ShowPlaytimeChart();
    }
    private void UpdateFavorite()
    {
        if (_game is null) return;
        FavoriteGlyph.Glyph = _game.Favorite ? "" : "";
        // Filled and gold when it's a favorite, like the star on its cover; the normal text color otherwise.
        if (_game.Favorite) FavoriteGlyph.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["FavoriteBrush"];
        else FavoriteGlyph.ClearValue(IconElement.ForegroundProperty);
        var label = _game.Favorite ? "Remove from favorites" : "Add to favorites";
        ToolTipService.SetToolTip(FavoriteButton, label);
        AutomationProperties.SetName(FavoriteButton, label);
    }

    private void UpdateSettingsRows()
    {
        if (_game is null) return;

        _loading = true;
        try
        {
            HideDelayBox.Items.Clear();
            HideDelayBox.Items.Add(new ComboBoxItem { Content = $"Default ({Format.Delay(_settings.HideDelaySeconds)})", Tag = -1 });
            foreach (int seconds in DelayChoices) HideDelayBox.Items.Add(new ComboBoxItem { Content = Format.Delay(seconds), Tag = seconds });
            int selected = _game.HideDelaySeconds ?? -1;
            HideDelayBox.SelectedItem = HideDelayBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == selected)
                ?? HideDelayBox.Items[0];
        }
        finally
        {
            _loading = false;
        }

        ProcessText.Text = _game.ProcessName ?? (_game.InstallDir is not null
            ? "Auto-detected: any program in the install folder"
            : "Auto-detected");
        ToolTipService.SetToolTip(ProcessText, _game.ProcessName is null ? _game.InstallDir : null);
        AutoProcessItem.IsEnabled = _game.ProcessName is not null;

        CoverText.Text = _game.CustomCover is not null ? "Custom image"
            : _game.Source == GameSource.Steam ? "From Steam"
            : "Generated";
        DefaultCoverItem.IsEnabled = _game.CustomCover is not null;
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => BackRequested?.Invoke();

    private void OnPlayClick(object sender, RoutedEventArgs e) => Raise(GameAction.Play);

    private void OnFolderClick(object sender, RoutedEventArgs e) => Raise(GameAction.OpenFolder);

    private void OnStoreClick(object sender, RoutedEventArgs e) => Raise(GameAction.StorePage);

    private void OnHideClick(object sender, RoutedEventArgs e) => Raise(GameAction.Hide);

    private void OnFavoriteClick(object sender, RoutedEventArgs e) => Raise(GameAction.ToggleFavorite);

    private void Raise(GameAction action)
    {
        if (_game is not null) ActionRequested?.Invoke(_game, action);
    }

    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        if (_game is not null) GameMenu.Show(MoreButton, null, _game, includePlayAndDetails: false, Raise);
    }

    private void OnHideDelayChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _game is null || HideDelayBox.SelectedItem is not ComboBoxItem { Tag: int seconds }) return;
        _game.HideDelaySeconds = seconds < 0 ? null : seconds;
        Edited(_game);
    }

    /// <summary>Lets the user point at the game's real exe, for launchers the folder match can't follow.</summary>
    private void OnPickProcess(object sender, RoutedEventArgs e)
    {
        if (_game is not { } game) return;
        var path = FilePicker.Open(this, $"Choose the program {game.Name} runs as", "Programs (*.exe)|*.exe",
            GameActions.InstallFolder(game));
        if (path is null) return;
        game.ProcessName = Path.GetFileName(path);
        Edited(game);
    }

    private void OnAutoProcess(object sender, RoutedEventArgs e)
    {
        if (_game is not { } game) return;
        game.ProcessName = null;
        Edited(game);
    }

    private void OnPickCover(object sender, RoutedEventArgs e)
    {
        if (_game is not { } game) return;
        var path = FilePicker.Open(this, $"Choose a cover for {game.Name}",
            "Images (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp");
        if (path is null) return;
        try
        {
            game.CustomCover = CoverCache.ImportCustomCover(game, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return; // couldn't copy the image; keep the current cover
        }
        Edited(game);
        SetArt(Cover, game);
    }

    private void OnDefaultCover(object sender, RoutedEventArgs e)
    {
        if (_game is not { } game) return;
        CoverCache.ClearCustomCover(game);
        Edited(game);
        SetArt(Cover, game);
    }

    private void Edited(Game game)
    {
        UpdateSettingsRows();
        GameEdited?.Invoke(game);
    }
}
