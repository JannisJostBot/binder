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
        EnterHintText.Text = Strings.EnterKey;
        AutomationProperties.SetAccessibilityView(EnterHint, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
    }

    /// <summary>The search's top match shows an "Enter" key (Enter in the search box plays it).</summary>
    public void SetEnterHint(bool on)
    {
        EnterHint.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        FitName();
    }

    private void OnRowSizeChanged(object sender, SizeChangedEventArgs e) => FitName();

    /// <summary>The name gets what the line has left after the star and the Enter key, so it trims with "…".</summary>
    private void FitName()
    {
        double others = 0;
        foreach (var part in new FrameworkElement[] { FavoriteIcon, EnterHint })
        {
            if (part.Visibility != Visibility.Visible) continue;
            part.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            others += part.DesiredSize.Width + NameLine.Spacing;
        }
        // The column's width, not the panel's: an overflowing StackPanel reports its content's width.
        double room = Row.ColumnDefinitions[1].ActualWidth;
        if (room > 0) NameText.MaxWidth = Math.Max(0, room - others);
    }

    /// <summary>The full name as a tooltip only when it's cut off, as on the covers.</summary>
    private void OnNameTrimmedChanged(TextBlock sender, IsTextTrimmedChangedEventArgs args) =>
        ToolTipService.SetToolTip(sender, sender.IsTextTrimmed ? sender.Text : null);

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
        if (NameText.IsTextTrimmed) ToolTipService.SetToolTip(NameText, game.Name); // recycled row; a change of trimming fires the event
        FavoriteIcon.Visibility = game.Favorite ? Visibility.Visible : Visibility.Collapsed;
        FitName();
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
