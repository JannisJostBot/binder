using GameShelf.Models;
using GameShelf.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GameShelf.Views;

/// <summary>
/// The launcher's icon in the top-left of a cover (spec §3): a 28 px near-solid dark rounded square with a thin
/// light edge and the launcher's own 18 px icon, or a Fluent glyph if that can't be read. None for manual games and
/// shortcuts. The name is in the tooltip and for screen readers, so identity never depends on the logo alone.
/// </summary>
public sealed partial class LauncherBadge : Grid
{
    // One UI bitmap per launcher, shared by every badge (UI thread only).
    private static readonly Dictionary<GameSource, ImageSource> Images = [];

    private GameSource _source = GameSource.Manual;

    // WinUI's Border is sealed; a Grid with one child does the same here.
    private UIElement? Child
    {
        get => Children.Count > 0 ? Children[0] : null;
        set
        {
            Children.Clear();
            if (value is not null) Children.Add(value);
        }
    }

    public LauncherBadge()
    {
        Width = 28;
        Height = 28;
        CornerRadius = new CornerRadius(7);
        Background = (Brush)Application.Current.Resources["BadgeBrush"];
        BorderBrush = (Brush)Application.Current.Resources["BadgeStrokeBrush"];
        BorderThickness = new Thickness(1);
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(6);
        Visibility = Visibility.Collapsed;
    }

    public GameSource Source
    {
        get => _source;
        set
        {
            if (_source == value && Child is not null) return;
            _source = value;
            Update();
        }
    }

    private void Update()
    {
        var source = _source;
        if (!LauncherIcons.HasBadge(source))
        {
            Visibility = Visibility.Collapsed;
            Child = null;
            return;
        }
        Visibility = Visibility.Visible;
        var name = Label(source);
        ToolTipService.SetToolTip(this, name);
        AutomationProperties.SetName(this, name);

        var icon = LauncherIcons.Get(source, loaded =>
        {
            if (_source == source) Show(source, loaded); // the tile may have been recycled for another game meanwhile
        });
        Show(source, icon);
    }

    private void Show(GameSource source, IconPixels? icon)
    {
        if (icon is not null)
        {
            if (!Images.TryGetValue(source, out var image)) Images[source] = image = Bitmaps.FromPixels(icon);
            Child = new Image { Source = image, Width = 18, Height = 18 };
            return;
        }
        Child = new FontIcon
        {
            Glyph = "", // Game
            FontSize = 14,
            Foreground = new SolidColorBrush(Colors.White),
        };
    }

    /// <summary>The accessible launcher name.</summary>
    public static string Label(GameSource source) => Format.LauncherName(source);

    /// <summary>A Grid isn't in the UI Automation tree by itself; this exposes the badge as an image named after the launcher.</summary>
    protected override AutomationPeer OnCreateAutomationPeer() => new BadgePeer(this);

    private sealed partial class BadgePeer(LauncherBadge owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;
        protected override string GetClassNameCore() => nameof(LauncherBadge);
        protected override bool IsControlElementCore() => owner.Visibility == Visibility.Visible;
    }
}
