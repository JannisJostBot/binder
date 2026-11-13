using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Binder.Views;

/// <summary>
/// Windows 11's Acrylic behind the panel, as on Windows' own flyouts. Unlike WinUI's DesktopAcrylicBackdrop it
/// stays Acrylic when the panel isn't the focused window (the desktop panel rarely is). Windows switches to a
/// solid color by itself when Transparency effects are off, in high contrast and in battery saver; the light or
/// dark tint follows the app theme.
/// </summary>
public sealed partial class PanelBackdrop : SystemBackdrop
{
    private DesktopAcrylicController? _controller;
    private SystemBackdropConfiguration? _configuration;
    private FrameworkElement? _root;

    /// <summary>False on Windows 10, where the panel paints a solid background instead.</summary>
    public static bool IsSupported => DesktopAcrylicController.IsSupported();

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        if (!IsSupported) return;
        _configuration = new SystemBackdropConfiguration { IsInputActive = true };
        _controller = new DesktopAcrylicController();
        _controller.AddSystemBackdropTarget(connectedTarget);
        _controller.SetSystemBackdropConfiguration(_configuration);
        _root = xamlRoot.Content as FrameworkElement;
        if (_root is not null)
        {
            _root.ActualThemeChanged += OnThemeChanged;
            OnThemeChanged(_root, null);
        }
    }

    private void OnThemeChanged(FrameworkElement sender, object? args)
    {
        if (_configuration is null) return;
        _configuration.Theme = sender.ActualTheme switch
        {
            ElementTheme.Light => SystemBackdropTheme.Light,
            ElementTheme.Dark => SystemBackdropTheme.Dark,
            _ => SystemBackdropTheme.Default,
        };
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        if (_root is not null) _root.ActualThemeChanged -= OnThemeChanged;
        _root = null;
        _controller?.RemoveSystemBackdropTarget(disconnectedTarget);
        _controller?.Dispose();
        _controller = null;
        _configuration = null;
    }
}
