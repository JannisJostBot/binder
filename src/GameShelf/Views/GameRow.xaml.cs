using GameShelf.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GameShelf.Views;

/// <summary>One game in the shelf's list view. Recycled like <see cref="GameTile"/>.</summary>
public sealed partial class GameRow : UserControl
{
    public GameRow()
    {
        InitializeComponent();
    }

    public void Show(Game game, int order)
    {
        Cover.Order = order; // covers load from the top of the list down
        if (ReferenceEquals(Cover.Game, game)) Cover.Refresh();
        else Cover.Game = game;
        NameText.Text = game.Name;
        FavoriteIcon.Visibility = game.Favorite ? Visibility.Visible : Visibility.Collapsed;
        SourceText.Text = game.SourceLabel;
        LastPlayedText.Text = game.LastPlayedText;
    }
}
