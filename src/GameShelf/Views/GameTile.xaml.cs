using System.Numerics;
using GameShelf.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace GameShelf.Views;

/// <summary>
/// One cover on the shelf grid: art, launcher badge, favorite star and title. Clicking the cover plays (the grid's
/// ItemClick). On hover, keyboard focus or controller focus it lifts with a shadow, a fine edge and a fade at the
/// bottom, and shows a round Play mark and the Details (i) button (board 26, "Media tile"). The grid recycles tiles,
/// so <see cref="Show"/> may be called with another game at any time.
/// </summary>
public sealed partial class GameTile : UserControl
{
    // Fluent's control timings (ControlFastAnimationDuration, ControlFasterAnimationDuration).
    private static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(167), Faster = TimeSpan.FromMilliseconds(83);
    private const float Raised = 1.04f, Pressed = 1.01f, ShadowDepth = 24, PlayRise = 4;

    // Read when a tile changes, never polled: "Animation effects" and high contrast can change while the panel is open.
    private static readonly Windows.UI.ViewManagement.UISettings UiSettings = new();
    private static readonly Windows.UI.ViewManagement.AccessibilitySettings Accessibility = new();

    // The shadow stays on the tile raised last (at depth 0 once it's lowered, so it doesn't show) and is taken off
    // when another tile rises, so the grid has only one or two at a time.
    private static GameTile? _shadowOwner;

    private readonly Vector3Transition _scaleTransition = new() { Duration = Fast };
    private readonly Vector3Transition _liftTransition = new() { Duration = Fast };
    private readonly Vector3Transition _riseTransition = new() { Duration = Fast };
    private readonly ScalarTransition _fadePlay = new() { Duration = Faster };
    private readonly ScalarTransition _fadeScrim = new() { Duration = Faster };
    private readonly ScalarTransition _fadeEdge = new() { Duration = Faster };
    private readonly ScalarTransition _fadeOutline = new() { Duration = Faster };
    private readonly ScalarTransition _fadeDetails = new() { Duration = Faster };
    private Game? _game;
    private bool _hover, _pressed, _keyFocus, _padFocus;

    public GameTile()
    {
        InitializeComponent();
        Cover.ImageChanged += hasImage => PlaceholderTitle.Visibility = hasImage ? Visibility.Collapsed : Visibility.Visible;
        PlayMark.Translation = new Vector3(0, PlayRise, 0);
        // The grid item handles the press itself, so listen to handled events too.
        Hit.AddHandler(PointerPressedEvent, new PointerEventHandler(OnPointerPressed), true);
        Hit.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnPointerReleased), true);
    }

    private void OnTitleTrimmedChanged(TextBlock sender, IsTextTrimmedChangedEventArgs args) =>
        ToolTipService.SetToolTip(sender, sender.IsTextTrimmed ? sender.Text : null);

    private void OnCoverSizeChanged(object sender, SizeChangedEventArgs e) =>
        CoverArea.CenterPoint = new Vector3((float)e.NewSize.Width / 2, (float)e.NewSize.Height / 2, 0);

    /// <summary>Called when the Details (i) button is pressed.</summary>
    public Action<Game>? DetailsRequested { get; set; }

    public void Show(Game game, double width, double height, int order)
    {
        Width = width;
        Cover.Height = height;
        Cover.DecodeWidth = width;
        // Recycled: the shelf sets focus again when this item gets it. Straight to rest, without animating.
        _hover = _pressed = _keyFocus = false;
        SetPadFocus(false, animate: false);
        Apply(animate: false);
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
        if (TitleText.IsTextTrimmed) ToolTipService.SetToolTip(TitleText, game.Name); // recycled tile; a change of trimming fires the event
        PlaceholderTitle.Text = game.Name;
        PlaceholderTitle.Visibility = Cover.HasImage ? Visibility.Collapsed : Visibility.Visible;
        Badge.Source = game.Source;
        FavoriteBadge.Visibility = game.Favorite ? Visibility.Visible : Visibility.Collapsed;
        Shine.SetOn(game.Favorite && App.Settings.ShinyFavorites && !Accessibility.HighContrast);
        AutomationProperties.SetName(DetailsButton, "Details for " + game.Name);
    }

    /// <summary>The sheen follows the pointer over a favourite (§13 d); still with "Animation effects" off.</summary>
    private void OnCoverPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!Shine.IsOn || !UiSettings.AnimationsEnabled || CoverArea.ActualWidth <= 0) return;
        var p = e.GetCurrentPoint(CoverArea).Position;
        Shine.Follow((float)(p.X / CoverArea.ActualWidth), (float)(p.Y / CoverArea.ActualHeight));
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _hover = true;
        Apply(animate: true);
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _hover = _pressed = false;
        Apply(animate: true);
        Shine.Rest(UiSettings.AnimationsEnabled);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _pressed = true;
        Apply(animate: true);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_pressed) return;
        _pressed = false;
        Apply(animate: true);
    }

    /// <summary>Keyboard focus (Tab, arrows): the hover look inside Windows' focus rectangle. Not for focus set in
    /// code (coming back from details), so a mouse user sees no change.</summary>
    public void SetKeyFocus(bool on)
    {
        if (_keyFocus == on) return;
        _keyFocus = on;
        Apply(animate: true);
    }

    /// <summary>Controller focus (board 3): the hover look plus a bold ring and a bold title.</summary>
    public void SetPadFocus(bool on) => SetPadFocus(on, animate: true);

    private void SetPadFocus(bool on, bool animate)
    {
        if (_padFocus == on) return;
        _padFocus = on;
        PadRing.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        TitleText.FontWeight = on ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        Apply(animate);
    }

    private bool IsUp => _hover || _keyFocus || _padFocus;

    /// <summary>Sets the raised or resting look. The transitions run only while a value changes (167 ms for the
    /// lift and Play's rise, 83 ms for fades and the press), so nothing runs while idle; with "Animation effects"
    /// off it's instant. High contrast: no shadow, fade or edge, a highlight outline instead.</summary>
    private void Apply(bool animate)
    {
        bool up = IsUp, contrast = up && Accessibility.HighContrast;
        SetTransitions(animate && UiSettings.AnimationsEnabled);
        _scaleTransition.Duration = _pressed ? Faster : Fast;

        if (up && !contrast && CoverArea.Shadow is null)
        {
            if (_shadowOwner is { } owner && owner != this && !owner.IsUp) owner.CoverArea.Shadow = null;
            CoverArea.Shadow = new Microsoft.UI.Xaml.Media.ThemeShadow();
            _shadowOwner = this;
        }
        else if (contrast) CoverArea.Shadow = null;

        float scale = !up ? 1 : _pressed ? Pressed : Raised;
        CoverArea.Scale = new Vector3(scale, scale, 1);
        CoverArea.Translation = new Vector3(0, 0, up && !contrast ? ShadowDepth : 0);
        Scrim.Opacity = Edge.Opacity = up && !contrast ? 1 : 0;
        HighContrastOutline.Opacity = contrast ? 1 : 0;
        PlayMark.Opacity = up ? 1 : 0;
        PlayMark.Translation = new Vector3(0, up ? 0 : PlayRise, 0);
        DetailsButton.Opacity = up ? 1 : 0;
        DetailsButton.IsHitTestVisible = up;
    }

    private void SetTransitions(bool on)
    {
        CoverArea.ScaleTransition = on ? _scaleTransition : null;
        CoverArea.TranslationTransition = on ? _liftTransition : null;
        PlayMark.TranslationTransition = on ? _riseTransition : null;
        PlayMark.OpacityTransition = on ? _fadePlay : null;
        Scrim.OpacityTransition = on ? _fadeScrim : null;
        Edge.OpacityTransition = on ? _fadeEdge : null;
        HighContrastOutline.OpacityTransition = on ? _fadeOutline : null;
        DetailsButton.OpacityTransition = on ? _fadeDetails : null;
    }

    private void OnDetailsClick(object sender, RoutedEventArgs e)
    {
        if (_game is not null) DetailsRequested?.Invoke(_game);
    }
}
