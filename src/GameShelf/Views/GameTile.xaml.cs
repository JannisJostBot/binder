using GameShelf.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace GameShelf.Views;

/// <summary>
/// One cover on the shelf grid: art, launcher badge, favorite star and title. Clicking the cover plays (the grid's
/// ItemClick); on hover it lifts, shows a "Play" label and a Details (i) button. The grid recycles tiles, so
/// <see cref="Show"/> may be called with another game at any time.
/// </summary>
public sealed partial class GameTile : UserControl
{
    private Game? _game;

    public GameTile()
    {
        InitializeComponent();
        Cover.ImageChanged += hasImage => PlaceholderTitle.Visibility = hasImage ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Called when the Details (i) button is pressed.</summary>
    public Action<Game>? DetailsRequested { get; set; }

    public void Show(Game game, double width, double height, int order)
    {
        Width = width;
        Cover.Height = height;
        Cover.DecodeWidth = width;
        SetHover(false);
        Cover.Order = order; // covers load from the top of the grid down
        if (ReferenceEquals(_game, game))
        {
            Cover.Refresh(); // same game, its cover or favorite may have changed
        }
        else
        {
            _game = game;
            Cover.Game = game;
        }
        TitleText.Text = game.Name;
        PlaceholderTitle.Text = game.Name;
        PlaceholderTitle.Visibility = Cover.HasImage ? Visibility.Collapsed : Visibility.Visible;
        Badge.Source = game.Source;
        FavoriteBadge.Visibility = game.Favorite ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(DetailsButton, "Details for " + game.Name);
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e) => SetHover(true);

    private void OnPointerExited(object sender, PointerRoutedEventArgs e) => SetHover(false);

    /// <summary>Instant, no animation: nothing runs while idle, and it's the same with animations off.</summary>
    private void SetHover(bool on)
    {
        Overlay.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        DetailsButton.Opacity = on ? 1 : 0;
        DetailsButton.IsHitTestVisible = on;
        Lift.ScaleX = Lift.ScaleY = on ? 1.03 : 1;
    }

    private void OnDetailsClick(object sender, RoutedEventArgs e)
    {
        if (_game is not null) DetailsRequested?.Invoke(_game);
    }
}
