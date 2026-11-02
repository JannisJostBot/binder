using GameShelf.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace GameShelf.Views;

/// <summary>
/// One cover on the shelf grid: art, launcher badge, favorite star, title, and a Play button on hover. The
/// grid recycles tiles, so <see cref="Show"/> may be called with another game at any time.
/// </summary>
public sealed partial class GameTile : UserControl
{
    private Game? _game;

    public GameTile()
    {
        InitializeComponent();
        Cover.ImageChanged += hasImage => PlaceholderTitle.Visibility = hasImage ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Called when the hover Play button is pressed.</summary>
    public Action<Game>? PlayRequested { get; set; }

    public void Show(Game game, double width, double height, int order)
    {
        Width = width;
        Cover.Height = height;
        Cover.DecodeWidth = width;
        Overlay.Visibility = Visibility.Collapsed;
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
        AutomationProperties.SetName(PlayButton, "Play " + game.Name);
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e) => Overlay.Visibility = Visibility.Visible;

    private void OnPointerExited(object sender, PointerRoutedEventArgs e) => Overlay.Visibility = Visibility.Collapsed;

    private void OnPlayClick(object sender, RoutedEventArgs e)
    {
        if (_game is not null) PlayRequested?.Invoke(_game);
    }
}
