using Binder.Models;
using Binder.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Binder.Views;

/// <summary>
/// A rounded cover. Loads its game's cover asynchronously at <see cref="DecodeWidth"/> (effective pixels,
/// scaled to the monitor) and paints it as the border's background, so it's clipped to the corners. The
/// bitmap lives only as long as the cover shows that game, so recycled tiles release covers that scroll away.
/// Without store art it shows the generated cover (<see cref="FallbackArt"/>).
/// </summary>
public sealed partial class CoverView : Grid
{
    private int _version;
    private Game? _game;

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

    public CoverView()
    {
        Background = (Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"];
        CornerRadius = new CornerRadius(8);
        Loaded += (_, _) =>
        {
            if (_game is not null && !HasImage) Reload(); // DPI is known once in the tree
        };
    }

    /// <summary>Raised when the cover or generated art appears or goes back to the placeholder.</summary>
    public event Action<bool>? ImageChanged;

    public Game? Game
    {
        get => _game;
        set
        {
            if (ReferenceEquals(_game, value)) return;
            _game = value;
            Reload();
        }
    }

    /// <summary>True once the cover image or generated art is shown; false while the placeholder is visible.</summary>
    public bool HasImage { get; private set; }

    private double _decodeWidth = 120;

    /// <summary>Width the cover is displayed at, in effective pixels.</summary>
    public double DecodeWidth
    {
        get => _decodeWidth;
        set
        {
            if (_decodeWidth == value) return;
            _decodeWidth = value;
            if (_game is not null) Reload();
        }
    }

    /// <summary>Load order: lower goes first (the grid sets the item index; others load before the grid).</summary>
    public int Order { get; set; } = -1;

    /// <summary>Portrait cover (default) or the wide hero banner. Set before <see cref="Game"/>.</summary>
    public ArtKind Kind { get; set; } = ArtKind.Cover;

    /// <summary>Reloads the art, e.g. after the game's custom cover changed (the cache must be invalidated first).</summary>
    public void Refresh() => Reload();

    private async void Reload()
    {
        int version = ++_version;
        var game = _game;
        if (game is null)
        {
            ShowPlaceholder();
            return;
        }

        double scale = XamlRoot?.RasterizationScale ?? 1;
        int pixelWidth = (int)Math.Ceiling(DecodeWidth * scale);
        // Already in memory (shown before, or recycled): draw it at once, no placeholder flash.
        if (CoverCache.TryGetCached(game, pixelWidth, Kind) is { } cached)
        {
            ShowImage(cached);
            return;
        }
        if (CoverCache.TryGetFallback(game, Kind) is { } known)
        {
            ShowFallback(game, known);
            return;
        }

        ShowPlaceholder();
        ImageSource? image;
        FallbackArtInfo? art = null;
        using (var slot = await CoverQueue.WaitAsync(Order, () => version != _version))
        {
            if (slot is null || version != _version) return; // recycled for another game while waiting
            image = await CoverCache.LoadAsync(game, pixelWidth, Kind);
            if (image is null)
            {
                // No custom cover and no store art: the game's icon on its color, or a monogram.
                try
                {
                    art = await CoverCache.GetFallbackAsync(game);
                }
                catch (Exception e) when (e is InvalidOperationException or ArgumentException or IOException or FormatException)
                {
                    // A cover that can't be drawn must never take the widget down: log it, keep the placeholder.
                    ErrorLog.Write(e, "cover");
                    return;
                }
            }
        }
        if (version != _version) return;
        if (image is not null) ShowImage(image);
        else if (art is not null) ShowFallback(game, art);
    }

    private void ShowImage(ImageSource image)
    {
        Child = null;
        Background = new ImageBrush { ImageSource = image, Stretch = Stretch.UniformToFill };
        SetHasImage(true);
    }

    private void ShowFallback(Game game, FallbackArtInfo art)
    {
        var (background, content) = Kind == ArtKind.Hero ? FallbackArt.Banner(game, art) : FallbackArt.Cover(game, art);
        Background = background;
        Child = content;
        SetHasImage(true); // the generated art includes the title
    }

    private void ShowPlaceholder()
    {
        Child = null;
        Background = (Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"];
        SetHasImage(false);
    }

    private void SetHasImage(bool value)
    {
        if (HasImage == value) return;
        HasImage = value;
        ImageChanged?.Invoke(value);
    }
}
