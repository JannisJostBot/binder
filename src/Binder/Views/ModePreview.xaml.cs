using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Binder.Views;

/// <summary>A small, decorative picture of the desktop in one display mode (setup and Settings).</summary>
public sealed partial class ModePreview : UserControl
{
    public ModePreview() => InitializeComponent();

    /// <summary>True for "On the desktop", false for the flyout.</summary>
    public bool IsDesktop
    {
        get => DesktopScene.Visibility == Visibility.Visible;
        set
        {
            DesktopScene.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            FlyoutScene.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            // The tray icon is how the flyout opens; on the desktop it's just one more icon.
            TrayIcon.Opacity = value ? 0.4 : 1;
        }
    }
}
