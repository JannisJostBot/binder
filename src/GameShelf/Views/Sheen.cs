using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace GameShelf.Views;

/// <summary>
/// The shiny sleeve on favourite covers (proposals §13 d, prototype "Medium"). It has a faint diagonal rainbow
/// foil, a soft white glare and a thin light edge, all plain alpha composition layers clipped to the cover's
/// corners; blend modes would need Win2D, which isn't approved. At rest the foil and glare sit still in the top
/// left. While the pointer moves over the cover they follow it (<see cref="Follow"/>, set from the tile's
/// PointerMoved). On leave they ease back to rest in 0.5 s. No timers: composition animations run only on leave.
/// Created on first use and kept, so a tile recycled to a non-favourite just collapses it.
/// </summary>
public sealed partial class Sheen : Grid
{
    private const float Radius = 8;
    private const float RestX = 0.28f, RestY = 0.18f;
    // Tuned against the prototype's "Medium" (which uses colour-dodge and overlay): plain alpha needs less.
    private const float FoilRest = 0.07f, FoilFollow = 0.20f, GlareRest = 0.04f, GlareFollow = 0.16f;
    private const float GlareSize = 1.7f; // of the cover's width
    private static readonly TimeSpan Ease = TimeSpan.FromMilliseconds(500), Fade = TimeSpan.FromMilliseconds(300);

    private static readonly Windows.UI.Color[] Foil =
    [
        Windows.UI.Color.FromArgb(255, 0xFF, 0x6F, 0xB5), Windows.UI.Color.FromArgb(255, 0xFF, 0xD3, 0x6E),
        Windows.UI.Color.FromArgb(255, 0x6F, 0xFF, 0xC1), Windows.UI.Color.FromArgb(255, 0x6E, 0xC8, 0xFF),
        Windows.UI.Color.FromArgb(255, 0xB6, 0x8C, 0xFF),
    ];

    private readonly Border _host = new();
    private ContainerVisual? _root;
    private SpriteVisual? _foil, _glare;
    private CompositionRoundedRectangleGeometry? _clip;
    private Vector2 _size;
    private float _x = RestX, _y = RestY;
    private bool _following;

    public Sheen()
    {
        IsHitTestVisible = false;
        Visibility = Visibility.Collapsed;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        Children.Add(_host);
        Children.Add(new Border
        {
            CornerRadius = new CornerRadius(Radius),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(72, 255, 255, 255)),
        });
        SizeChanged += (_, e) =>
        {
            _size = new Vector2((float)e.NewSize.Width, (float)e.NewSize.Height);
            Layout();
        };
    }

    public bool IsOn => Visibility == Visibility.Visible;

    /// <summary>On for a favourite (when "Shiny favourites" is on and not in high contrast); at rest either way.</summary>
    public void SetOn(bool on)
    {
        Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (!on) return;
        Ensure();
        Place(RestX, RestY, FoilRest, GlareRest, animate: false);
        _following = false;
    }

    /// <summary>The pointer at (x, y), as fractions of the cover: the foil shifts and the glare sits under it.</summary>
    public void Follow(float x, float y)
    {
        if (!IsOn) return;
        Place(Math.Clamp(x, 0, 1), Math.Clamp(y, 0, 1), FoilFollow, GlareFollow, animate: false);
        _following = true;
    }

    /// <summary>The pointer left: back to rest, easing over 0.5 s (instantly with "Animation effects" off).</summary>
    public void Rest(bool animate)
    {
        if (!IsOn || !_following) return;
        _following = false;
        Place(RestX, RestY, FoilRest, GlareRest, animate);
    }

    private void Ensure()
    {
        if (_root is not null) return;
        var c = ElementCompositionPreview.GetElementVisual(_host).Compositor;
        _root = c.CreateContainerVisual();
        _clip = c.CreateRoundedRectangleGeometry();
        _clip.CornerRadius = new Vector2(Radius);
        _root.Clip = c.CreateGeometricClip(_clip);

        // A diagonal band of the five colours, repeated, on a sprite three covers big that shifts with the pointer.
        var foil = c.CreateLinearGradientBrush();
        foil.StartPoint = new Vector2(0, 0);
        foil.EndPoint = new Vector2(1, 0.47f); // about the prototype's 115°
        const int cycles = 5;
        for (int i = 0; i <= cycles * Foil.Length; i++)
            foil.ColorStops.Add(c.CreateColorGradientStop((float)i / (cycles * Foil.Length), Foil[i % Foil.Length]));
        _foil = c.CreateSpriteVisual();
        _foil.Brush = foil;

        var glare = c.CreateRadialGradientBrush();
        glare.ColorStops.Add(c.CreateColorGradientStop(0, Windows.UI.Color.FromArgb(255, 255, 255, 255)));
        glare.ColorStops.Add(c.CreateColorGradientStop(0.31f, Windows.UI.Color.FromArgb(64, 255, 255, 255)));
        glare.ColorStops.Add(c.CreateColorGradientStop(0.71f, Colors.Transparent));
        glare.ColorStops.Add(c.CreateColorGradientStop(1, Colors.Transparent));
        _glare = c.CreateSpriteVisual();
        _glare.Brush = glare;

        _root.Children.InsertAtTop(_foil);
        _root.Children.InsertAtTop(_glare);
        ElementCompositionPreview.SetElementChildVisual(_host, _root);
        Layout();
    }

    private void Layout()
    {
        if (_root is null || _size.X <= 0) return;
        _root.Size = _size;
        _clip!.Size = _size;
        _foil!.Size = _size * 3;
        float d = _size.X * GlareSize;
        _glare!.Size = new Vector2(d, d);
        Place(_x, _y, _foil.Opacity, _glare.Opacity, animate: false);
    }

    private Vector3 FoilOffset(float x, float y) => new(-_size.X * (1 + x), -_size.Y * (1 + y), 0);

    private Vector3 GlareOffset(float x, float y)
    {
        float half = _size.X * GlareSize / 2;
        return new Vector3(x * _size.X - half, y * _size.Y - half, 0);
    }

    private void Place(float x, float y, float foilOpacity, float glareOpacity, bool animate)
    {
        _x = x;
        _y = y;
        if (_foil is null || _glare is null) return;
        if (!animate)
        {
            foreach (var v in new Visual[] { _foil, _glare })
            {
                v.StopAnimation(nameof(Visual.Offset));
                v.StopAnimation(nameof(Visual.Opacity));
            }
            _foil.Offset = FoilOffset(x, y);
            _glare.Offset = GlareOffset(x, y);
            _foil.Opacity = foilOpacity;
            _glare.Opacity = glareOpacity;
            return;
        }
        var c = _foil.Compositor;
        var easeOut = c.CreateCubicBezierEasingFunction(new Vector2(0, 0), new Vector2(0.58f, 1));
        _foil.StartAnimation(nameof(Visual.Offset), Move(c, FoilOffset(x, y), easeOut));
        _glare.StartAnimation(nameof(Visual.Offset), Move(c, GlareOffset(x, y), easeOut));
        _foil.StartAnimation(nameof(Visual.Opacity), FadeTo(c, foilOpacity, easeOut));
        _glare.StartAnimation(nameof(Visual.Opacity), FadeTo(c, glareOpacity, easeOut));
    }

    private static Vector3KeyFrameAnimation Move(Compositor c, Vector3 to, CompositionEasingFunction ease)
    {
        var a = c.CreateVector3KeyFrameAnimation();
        a.InsertKeyFrame(1, to, ease);
        a.Duration = Ease;
        return a;
    }

    private static ScalarKeyFrameAnimation FadeTo(Compositor c, float to, CompositionEasingFunction ease)
    {
        var a = c.CreateScalarKeyFrameAnimation();
        a.InsertKeyFrame(1, to, ease);
        a.Duration = Fade;
        return a;
    }
}
