using Binder.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Binder.Views;

/// <summary>One game in the shelf's list view. Recycled like <see cref="GameTile"/>.</summary>
public sealed partial class GameRow : UserControl
{
    private Game? _game;

    public GameRow()
    {
        InitializeComponent();
        ToolTipService.SetToolTip(DetailsButton, Strings.Details);
    }

    /// <summary>Called when the Details (i) button is pressed. Clicking the row itself plays.</summary>
    public Action<Game>? DetailsRequested { get; set; }

    public void Show(Game game, int order)
    {
        _game = game;
        SetHover(false);
        AutomationProperties.SetName(DetailsButton, Strings.Format(Strings.DetailsFor, game.Name));
        Cover.Order = order; // covers load from the top of the list down
        if (ReferenceEquals(Cover.Game, game)) Cover.Refresh();
        else Cover.Game = game;
        NameText.Text = game.Name;
        FavoriteIcon.Visibility = game.Favorite ? Visibility.Visible : Visibility.Collapsed;
        SourceText.Text = game.SourceLabel;
        LastPlayedText.Text = game.LastPlayedText;
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e) => SetHover(true);

    private void OnPointerExited(object sender, PointerRoutedEventArgs e) => SetHover(false);

    private void SetHover(bool on)
    {
        DetailsButton.Opacity = on ? 1 : 0;
        DetailsButton.IsHitTestVisible = on;
    }

    private void OnDetailsClick(object sender, RoutedEventArgs e)
    {
        if (_game is not null) DetailsRequested?.Invoke(_game);
    }
}
