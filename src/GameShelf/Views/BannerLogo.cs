using GameShelf.Models;
using GameShelf.Services;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace GameShelf.Views;

/// <summary>
/// The game's own logo on a banner (proposals §13 a): Steam's transparent logo.png, at most
/// <see cref="MaxLogoHeight"/> high and <see cref="MaxWidthShare"/> of the banner wide, aspect kept, at the
/// bottom left, with a soft static shadow cut from the logo's own shape so dark logos stay readable on dark art
/// (none in high contrast). Loaded only while a game is set; <see cref="LogoChanged"/> tells the page whether to
/// show the title text instead (no logo, or not loaded yet). Its screen-reader name is the game's title.
/// </summary>
public sealed partial class BannerLogo : Grid
{
    private readonly Image _image = new() { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
    private readonly Border _shadowHost = new() { IsHitTestVisible = false };
    private SpriteVisual? _shadow;
    private Game? _game;
    private int _load;

    /// <summary>Raised with true once a logo shows, false when there's none (or it was cleared).</summary>
    public event Action<bool>? LogoChanged;

    public double MaxLogoHeight { get; set; } = 56;
    public double MaxWidthShare { get; set; } = 0.6;
    public bool HasLogo { get; private set; }

    public BannerLogo()
    {
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Bottom;
        Visibility = Visibility.Collapsed;
        AutomationProperties.SetAccessibilityView(_shadowHost, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        Children.Add(_shadowHost);
        Children.Add(_image);
        _image.SizeChanged += (_, _) => UpdateShadow();
        SizeChanged += (_, _) => Fit();
    }

    /// <summary>The game whose logo to show; null releases it.</summary>
    public Game? Game
    {
        get => _game;
        set
        {
            if (ReferenceEquals(_game, value) && (HasLogo || value is null)) return;
            _game = value;
            _ = LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        int load = ++_load;
        var game = _game;
        Show(null);
        if (game is null || game.Source != GameSource.Steam) return;
        double scale = XamlRoot?.RasterizationScale ?? 1;
        double width = Parent is FrameworkElement { ActualWidth: > 0 } banner ? banner.ActualWidth * MaxWidthShare : 340;
        int pixelWidth = (int)Math.Ceiling(width * scale);
        var image = CoverCache.TryGetCached(game, pixelWidth, ArtKind.Logo) ?? await CoverCache.LoadAsync(game, pixelWidth, ArtKind.Logo);
        if (load != _load) return; // another game was set meanwhile
        Show(image);
    }

    private void Show(ImageSource? image)
    {
        _image.Source = image;
        HasLogo = image is not null;
        Visibility = HasLogo ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(_image, _game?.Name ?? "");
        if (_shadow is not null) _shadow.IsVisible = HasLogo;
        Fit();
        LogoChanged?.Invoke(HasLogo);
    }

    /// <summary>Height-limited, and no wider than its share of the banner (the parent's width).</summary>
    private void Fit()
    {
        _image.MaxHeight = MaxLogoHeight;
        if (Parent is FrameworkElement banner && banner.ActualWidth > 0) _image.MaxWidth = banner.ActualWidth * MaxWidthShare;
    }

    private void UpdateShadow()
    {
        if (!HasLogo || _image.ActualWidth <= 0) return;
        if (new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast) return;
        if (_shadow is null)
        {
            var compositor = ElementCompositionPreview.GetElementVisual(_shadowHost).Compositor;
            var shadow = compositor.CreateDropShadow();
            shadow.BlurRadius = 16;
            shadow.Offset = new System.Numerics.Vector3(0, 2, 0);
            shadow.Color = Windows.UI.Color.FromArgb(170, 0, 0, 0);
            _shadow = compositor.CreateSpriteVisual();
            _shadow.Shadow = shadow;
            ElementCompositionPreview.SetElementChildVisual(_shadowHost, _shadow);
        }
        // The logo's alpha is the shadow's shape; it follows a new image by itself.
        ((DropShadow)_shadow.Shadow).Mask = _image.GetAlphaMask();
        _shadow.Size = new System.Numerics.Vector2((float)_image.ActualWidth, (float)_image.ActualHeight);
        _shadow.Offset = new System.Numerics.Vector3(0, (float)(ActualHeight - _image.ActualHeight), 0);
        _shadow.IsVisible = true;
    }
}
