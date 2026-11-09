using System.ComponentModel;
using GameShelf.Models;
using GameShelf.Services;
using GameShelf.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace GameShelf.Views;

/// <summary>
/// Details for one game, shown in place of the grid inside the same panel. The state and what each control does
/// live in <see cref="DetailViewModel"/> (bound with x:Bind); this file and DetailView.Launch.cs only do view work:
/// the art, the rows drawn in code, the chart, the cards that open, focus and the file pickers.
/// </summary>
public sealed partial class DetailView : UserControl
{
    public DetailViewModel? ViewModel { get; private set; }

    private readonly Disclosure _launch;
    private readonly Disclosure _antiCheat;

    public DetailView()
    {
        InitializeComponent();
        Logo.LogoChanged += hasLogo => TitleText.Visibility = hasLogo ? Visibility.Collapsed : Visibility.Visible;
        _launch = new Disclosure(LaunchChevron, LaunchCard, LaunchBody, "launch options");
        _antiCheat = new Disclosure(AntiCheatChevron, AntiCheatCard, AntiCheatBody, "anti-cheat details");
        _antiCheat.Toggled += _ =>
        {
            if (ViewModel is not null) ViewModel.AntiCheatTouched = true;
        };
        SessionCrash.PlayRequested += _ => ViewModel?.Act(GameAction.Play);
        SessionCrash.Dismissed += record =>
        {
            SessionCrash.Visibility = Visibility.Collapsed;
            ViewModel?.CrashSeen(record);
        };
    }

    /// <summary>Connects the page to its owner once, before it's first shown.</summary>
    public void Initialize(IDetailActions actions)
    {
        if (ViewModel is not null) throw new InvalidOperationException("The detail page is already initialized.");
        ViewModel = new DetailViewModel(actions);
        ViewModel.PropertyChanged += OnViewModelChanged;
        ViewModel.NewGameShown += OnNewGameShown;
        ViewModel.CoverReplaced += () =>
        {
            if (ViewModel.Game is { } game) SetArt(Cover, game);
        };
        Bindings.Update();
    }

    /// <summary>The game being shown, or null.</summary>
    public Game? Game => ViewModel?.Game;

    public void FocusPlay() => PlayButton.Focus(FocusState.Programmatic);

    private void OnNewGameShown()
    {
        if (SessionCrash.Record is { } open && open.GameId != ViewModel!.Game!.Id) SessionCrash.Visibility = Visibility.Collapsed;
        _launch.Set(false);
        Scroller.ChangeView(null, 0, null, disableAnimation: true);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        var vm = ViewModel!;
        switch (e.PropertyName)
        {
            case null or "":
                if (vm.Game is not { } game) return;
                SetArt(Banner, game);
                SetArt(Cover, game);
                if (!ReferenceEquals(Logo.Game, game)) TitleText.Visibility = Visibility.Collapsed; // until the logo lookup answers
                Logo.Game = game;
                ShowFavorite();
                DrawSessions();
                DrawChart();
                DrawSuggestions();
                WriteExtraArgs();
                DrawAntiCheat();
                break;
            case nameof(DetailViewModel.ChartBars):
                DrawChart();
                break;
            case nameof(DetailViewModel.Suggestions):
                DrawSuggestions();
                break;
            case nameof(DetailViewModel.ExtraArgs):
                WriteExtraArgs();
                break;
            case nameof(DetailViewModel.Requirements):
                DrawAntiCheat();
                break;
        }
    }

    private static void SetArt(CoverView view, Game game)
    {
        // Same instance: force a reload (the art may have changed); the decoded image usually comes from memory.
        if (ReferenceEquals(view.Game, game)) view.Refresh();
        else view.Game = game;
    }

    /// <summary>Filled and gold when it's a favorite, like the star on its cover; the normal text color otherwise.</summary>
    private void ShowFavorite()
    {
        bool favorite = ViewModel!.IsFavorite;
        FavoriteGlyph.Glyph = favorite ? "" : "";
        if (favorite) FavoriteGlyph.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["FavoriteBrush"];
        else FavoriteGlyph.ClearValue(IconElement.ForegroundProperty);
    }

    /// <summary>The recent sessions, one row each with dividers (drawn here, not by a template).</summary>
    private void DrawSessions()
    {
        SessionsList.Children.Clear();
        foreach (var session in ViewModel!.Sessions)
        {
            if (SessionsList.Children.Count > 0) SessionsList.Children.Add(new Border { Style = (Style)Application.Current.Resources["RowDivider"] });
            SessionsList.Children.Add(SessionRow(session));
        }
    }

    private void DrawChart()
    {
        var vm = ViewModel!;
        if (vm.HasSessions) PlaytimeChart.Show(vm.ChartBars, vm.ChartName);
    }

    /// <summary>
    /// One session: a warning icon and a Details link when it crashed, a check when Game Shelf knows it ended
    /// normally, when it started and how long it lasted.
    /// </summary>
    private Grid SessionRow(SessionRowInfo info)
    {
        var secondary = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        var row = new Grid { MinHeight = 40, ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        if (info.Problem || info.KnownNormal)
        {
            var icon = new FontIcon
            {
                Glyph = info.Problem ? "" : "",
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[info.Problem ? "SystemFillColorCautionBrush" : "SystemFillColorSuccessBrush"],
            };
            AutomationProperties.SetName(icon, info.MarkLabel);
            ToolTipService.SetToolTip(icon, info.MarkLabel);
            row.Children.Add(icon);
        }

        var start = new TextBlock { Text = info.Start, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(start, 1);
        row.Children.Add(start);

        var length = new TextBlock { Text = info.Length, VerticalAlignment = VerticalAlignment.Center, Foreground = secondary };
        Grid.SetColumn(length, 2);
        row.Children.Add(length);

        if (info.Problem)
        {
            var details = new HyperlinkButton { Content = "Details", Padding = new Thickness(6, 2, 6, 2), VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(details, info.DetailsName);
            details.Click += (_, _) =>
            {
                var vm = ViewModel!;
                if (vm.Game is not { } game) return;
                SessionCrash.Show(game, info.Crash!, vm.AllSessions, vm.Crashes);
                SessionCrash.Visibility = Visibility.Visible;
                SessionCrash.StartBringIntoView();
            };
            Grid.SetColumn(details, 3);
            row.Children.Add(details);
        }
        return row;
    }

    private void OnPeriodChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (ViewModel is not null) ViewModel.ShowMonths = MonthsItem.IsSelected;
    }

    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.Game is { } game) GameMenu.Show(MoreButton, null, game, includePlayAndDetails: false, ViewModel.Act);
    }

    /// <summary>Lets the user point at the game's real exe, for launchers the folder match can't follow.</summary>
    private void OnPickProcess(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { Game: { } game } vm) return;
        var path = FilePicker.Open(this, $"Choose the program {game.Name} runs as", "Programs (*.exe)|*.exe", vm.InstallFolder);
        if (path is not null) vm.SetProcess(path);
    }

    private void OnPickCover(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { Game: { } game } vm) return;
        var path = FilePicker.Open(this, $"Choose a cover for {game.Name}",
            "Images (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp");
        if (path is not null) vm.SetCover(path);
    }
}
