using System.ComponentModel;
using System.Numerics;
using Binder.Library;
using Binder.ViewModels;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Binder.Views;

/// <summary>
/// Year in review (spec §28, boards 41–43). The year, its cards and where you are live in
/// <see cref="YearReviewViewModel"/>; this file draws the card on screen (<see cref="YearReviewCards"/>), slides it in
/// and plays its motion once (<see cref="YearReviewMotion"/>), and tells Narrator. Only the card on screen exists;
/// the one left behind is removed when it has slid out. Static while a card sits still.
/// </summary>
public sealed partial class YearReviewView : UserControl
{
    public YearReviewViewModel? ViewModel { get; private set; }

    private YearReviewMotion? _motion;
    private FrameworkElement? _shown;
    private CardSpec? _shownSpec;

    public YearReviewView()
    {
        InitializeComponent();
        AutomationProperties.SetName(this, Strings.YearInReview);
        ActualThemeChanged += (_, _) => ShowCard(animate: false);
    }

    /// <summary>Connects the page to its owner once, before it's first shown.</summary>
    public void Initialize(IYearReviewActions actions)
    {
        if (ViewModel is not null) throw new InvalidOperationException("The Year in review page is already initialized.");
        ViewModel = new YearReviewViewModel(actions);
        ViewModel.PropertyChanged += OnViewModelChanged;
        Bindings.Update();
    }

    /// <summary>How much slower the motion runs (the debug panel's Quarter speed); 1 otherwise.</summary>
    public float Slowdown { get; set; } = 1;

    public void FocusFirst() => NextButton.Focus(FocusState.Programmatic);

    /// <summary>Drops the card, its covers and the year's data (the page closed).</summary>
    public void Release()
    {
        _motion?.Stop();
        CardHost.Children.Clear();
        Progress.Children.Clear();
        Progress.ColumnDefinitions.Clear();
        _shown = null;
        _shownSpec = null;
        ViewModel?.Release();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case null or "": // a year loaded: its first card
                Bindings.Update();
                ShowCard(animate: true);
                break;
            case nameof(YearReviewViewModel.Card):
                ShowCard(animate: true);
                break;
        }
    }

    /// <summary>
    /// Shows the view model's card: the old one slides out (150 ms), the new one in, and its blocks come in once.
    /// With Animation effects off or in high contrast the card is simply replaced, at its final state.
    /// </summary>
    private void ShowCard(bool animate)
    {
        var vm = ViewModel;
        if (vm is null || Visibility != Visibility.Visible && animate) return;
        ShowProgress(vm);
        var spec = vm.Card;
        if (spec is null)
        {
            CardHost.Children.Clear();
            _shown = null;
            _shownSpec = null;
            return;
        }
        if (ReferenceEquals(spec, _shownSpec) && animate) return;

        _motion?.Stop();
        _motion ??= new YearReviewMotion(CardHost, Slowdown);
        bool moving = animate && YearReviewMotion.Enabled && CardHost.ActualWidth > 0;

        var (card, motion) = YearReviewCards.Build(spec, vm, Palette(spec, vm), CardHost.ActualWidth > 0 ? CardHost.ActualWidth : 528);
        ClipToCorners(card);
        var old = _shown;
        _shown = card;
        _shownSpec = spec;
        CardHost.Children.Add(card);

        if (!moving)
        {
            if (old is not null) CardHost.Children.Remove(old);
            YearReviewMotion.Still(motion);
            Announce(spec.Narration);
            return;
        }
        card.UpdateLayout(); // sizes for the scale and clip centers
        int delay = 0;
        if (old is not null)
        {
            delay = YearMotion.CardOutMs;
            _motion.Out(old, vm.Forward, () => CardHost.Children.Remove(old));
        }
        _motion.In(card, motion, vm.Forward, delay, vm.Theme.Motion);
        Announce(spec.Narration);
    }

    /// <summary>One segment per card, filled up to the one on screen.</summary>
    private void ShowProgress(YearReviewViewModel vm)
    {
        int count = vm.Cards.Count;
        if (Progress.ColumnDefinitions.Count != count)
        {
            Progress.Children.Clear();
            Progress.ColumnDefinitions.Clear();
            for (int i = 0; i < count; i++)
            {
                Progress.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var segment = new Border { CornerRadius = new CornerRadius(1.5) };
                Grid.SetColumn(segment, i);
                Progress.Children.Add(segment);
            }
        }
        var resources = Application.Current.Resources;
        var done = (Brush)resources["AccentFillColorDefaultBrush"];
        var open = (Brush)resources["ControlStrokeColorDefaultBrush"];
        for (int i = 0; i < Progress.Children.Count; i++)
            ((Border)Progress.Children[i]).Background = i <= vm.Index ? done : open;
    }

    /// <summary>
    /// The card's colors from the year's theme: its ground (the accent, the top game's cover color or neutral) mixed
    /// far toward the panel's dark or light base, a step lighter or darker per card. High contrast: the system's.
    /// </summary>
    private CardPalette Palette(CardSpec card, YearReviewViewModel vm)
    {
        var resources = Application.Current.Resources;
        var ui = new UISettings();
        if (new AccessibilitySettings().HighContrast)
        {
            var accentBrush = (SolidColorBrush)resources["AccentFillColorDefaultBrush"];
            return new CardPalette((Brush)resources["SolidBackgroundFillColorBaseBrush"], (Brush)resources["TextFillColorPrimaryBrush"],
                (Brush)resources["TextFillColorSecondaryBrush"], new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                (Brush)resources["ControlStrokeColorDefaultBrush"], accentBrush, accentBrush.Color, HighContrast: true);
        }
        bool dark = ActualTheme != ElementTheme.Light;
        uint accent = Rgb(ui.GetColorValue(UIColorType.Accent));
        uint? tint = vm.GameFor(card.TintGameId) is { } game
            ? Rgb(FallbackArt.Parse(game.CoverTint ?? CoverColors.MonogramTone(game.Name)))
            : null;
        uint background = YearColors.Background(vm.Theme.Ground, accent, tint, dark, (int)card.Kind);
        var highlight = ui.GetColorValue(dark ? UIColorType.AccentLight2 : UIColorType.AccentDark1);
        return new CardPalette(
            Brush(background),
            Brush(YearColors.Text(dark)),
            Brush(YearColors.SecondaryText(background, dark)),
            Brush(YearColors.Shape(background, dark)),
            Brush(YearColors.Muted(background, dark)),
            new SolidColorBrush(highlight),
            highlight,
            HighContrast: false);
    }

    private static uint Rgb(Color color) => (uint)(color.R << 16 | color.G << 8 | color.B);

    private static SolidColorBrush Brush(uint rgb) =>
        new(Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));

    /// <summary>The card's shapes and banner stay inside its rounded corners (a geometric clip on the compositor).</summary>
    private static void ClipToCorners(FrameworkElement card)
    {
        var visual = ElementCompositionPreview.GetElementVisual(card);
        var compositor = visual.Compositor;
        var shape = compositor.CreateRoundedRectangleGeometry();
        shape.CornerRadius = new Vector2(8);
        visual.Clip = compositor.CreateGeometricClip(shape);
        card.SizeChanged += (_, e) => shape.Size = new Vector2((float)e.NewSize.Width, (float)e.NewSize.Height);
    }

    /// <summary>Narrator reads the card's sentence when it comes in (a notification, not a live region: read once).</summary>
    private void Announce(string sentence)
    {
        var peer = FrameworkElementAutomationPeer.FromElement(this) ?? FrameworkElementAutomationPeer.CreatePeerForElement(this);
        peer?.RaiseNotificationEvent(AutomationNotificationKind.ActionCompleted, AutomationNotificationProcessing.ImportantMostRecent,
            sentence, "BinderYearInReviewCard");
    }

    /// <summary>→ or Space: the next card; ←: the one before. (Esc goes back to the shelf through the window.)</summary>
    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var vm = ViewModel;
        if (vm is null || e.Handled) return;
        var focused = FocusManager.GetFocusedElement(XamlRoot);
        if (focused is ComboBox or ComboBoxItem) return; // the year box uses the arrows itself
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Right:
                vm.NextCommand.Execute(null);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Left:
                vm.BackCommand.Execute(null);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Space when focused is not ButtonBase:
                vm.NextCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }
}
