using Binder.Views;
using Microsoft.UI.Xaml;

namespace Binder;

/// <summary>
/// The panel's pages other than the shelf, built in code on their first visit: the panel window is made anew at
/// every open ("Stay ready" releases it on close), and most opens never leave the shelf, so building every page each
/// time was work and memory for nothing. The <c>…Slot</c> fields are null until a page is built; the properties
/// below build it on first use. Checks whether a page is showing go through <see cref="Shown"/>, which never builds
/// one.
/// Not <c>x:Load="False"</c> in MainWindow.xaml: a Window's FindName doesn't realize those (tried on the real PC:
/// the page stayed unbuilt), unlike a UserControl's (GameTile's hover parts).
/// </summary>
public sealed partial class MainWindow
{
    private DetailView? DetailSlot;
    private SettingsView? SettingsSlot;
    private ImportView? ImportSlot;
    private SetupView? SetupSlot;
    private FreeSpaceView? FreeSpaceSlot;
    private GameModeView? GameModeSlot;

    private DetailView Detail => DetailSlot ?? Load(ref DetailSlot);
    private SettingsView SettingsPage => SettingsSlot ?? Load(ref SettingsSlot);
    private ImportView ImportPage => ImportSlot ?? Load(ref ImportSlot);
    private SetupView SetupPage => SetupSlot ?? Load(ref SetupSlot);
    private FreeSpaceView FreeSpacePage => FreeSpaceSlot ?? Load(ref FreeSpaceSlot);
    private GameModeView GameModePage => GameModeSlot ?? Load(ref GameModeSlot);

    /// <summary>
    /// Builds a page, collapsed, right above the shelf (below the notices and the launch toast, as in
    /// MainWindow.xaml), and connects its actions.
    /// </summary>
    private T Load<T>(ref T? slot) where T : UIElement, new()
    {
        var page = new T { Visibility = Visibility.Collapsed };
        Root.Children.Insert(Root.Children.IndexOf(Shelf) + 1, page);
        slot = page;
        switch (page)
        {
            case DetailView detail: detail.Initialize(this); break; // its actions: MainWindow.Detail.cs
            case SettingsView settings: settings.Initialize(this); break; // MainWindow.Settings.cs
            case GameModeView gameMode: gameMode.Initialize(this); break; // MainWindow.GameMode.cs
            case FreeSpaceView freeSpace: freeSpace.Initialize(this); break; // MainWindow.FreeSpace.cs
            case SetupView setup: setup.Initialize(this); break; // MainWindow.Setup.cs
            case ImportView import:
                import.Cancelled += ShowSettings;
                import.ImportRequested += ImportShortcuts;
                break;
        }
        return page;
    }

    /// <summary>Whether a page is built and showing (never builds it).</summary>
    private static bool Shown(UIElement? page) => page is { Visibility: Visibility.Visible };

    /// <summary>Shows a built page if it's <paramref name="shown"/>, else collapses it; an unbuilt one stays unbuilt.</summary>
    private static void SetShown(UIElement? page, UIElement shown)
    {
        if (page is not null) page.Visibility = page == shown ? Visibility.Visible : Visibility.Collapsed;
    }
}
