using GameShelf.Models;
using GameShelf.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Foundation;

namespace GameShelf.Views;

public enum GameAction { Play, Details, OpenFolder, StorePage, VerifyFiles, Uninstall, ToggleFavorite, Hide }

/// <summary>
/// The per-game menu, on a cover's right-click (or Menu key / Shift+F10) and the detail view's "…" button: a
/// WinUI menu flyout, as in Windows 11's own apps, built only when it opens.
/// </summary>
public static class GameMenu
{
    /// <summary>Opens the game menu at <paramref name="point"/> in <paramref name="target"/>, or under it when null.</summary>
    public static void Show(FrameworkElement target, Point? point, Game game, bool includePlayAndDetails, Action<GameAction> onAction)
    {
        var menu = Build(game, includePlayAndDetails, onAction);
        Open(menu, target, point);
    }

    /// <summary>Shows a menu: at a point (right-click), or below the target, right-aligned (a "…" button, the keyboard).</summary>
    public static void Open(MenuFlyout menu, FrameworkElement target, Point? point)
    {
        // The menu takes focus: click-away must not close the flyout panel under it.
        App.ModalDepth++;
        menu.Closed += (_, _) => App.ModalDepth--;
        menu.ShowAt(target, point is { } at
            ? new FlyoutShowOptions { Position = at }
            : new FlyoutShowOptions { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight });
    }

    private static MenuFlyout Build(Game game, bool includePlayAndDetails, Action<GameAction> onAction)
    {
        var menu = new MenuFlyout();
        void Add(string text, string glyph, GameAction action, string accessKey, bool enabled = true)
        {
            var item = new MenuFlyoutItem
            {
                Text = text,
                Icon = new FontIcon { Glyph = glyph },
                IsEnabled = enabled,
                AccessKey = accessKey,
            };
            item.Click += (_, _) => onAction(action);
            menu.Items.Add(item);
        }

        if (includePlayAndDetails)
        {
            Add("Play", "", GameAction.Play, "P");
            Add("Details", "", GameAction.Details, "D");
            menu.Items.Add(new MenuFlyoutSeparator());
        }
        Add("Open install folder", "", GameAction.OpenFolder, "F", enabled: GameActions.InstallFolder(game) is not null);
        if (GameActions.StoreUrl(game) is not null) Add("Store page", "", GameAction.StorePage, "S");
        if (game.Source == GameSource.Steam)
        {
            // Steam's own dialogs handle these (Uninstall asks for confirmation there).
            Add("Verify game files", "", GameAction.VerifyFiles, "V");
            Add("Uninstall in Steam…", "", GameAction.Uninstall, "U");
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        Add(game.Favorite ? "Remove from favorites" : "Add to favorites", game.Favorite ? "" : "",
            GameAction.ToggleFavorite, "A");
        Add("Hide from shelf", "", GameAction.Hide, "H");
        return menu;
    }

    /// <summary>Adds a radio item (one of a group) to a menu.</summary>
    public static void AddChoice(MenuFlyout menu, string text, string group, bool isChecked, Action click)
    {
        var item = new RadioMenuFlyoutItem { Text = text, GroupName = group, IsChecked = isChecked };
        item.Click += (_, _) => click();
        menu.Items.Add(item);
    }

    /// <summary>Adds a plain item to a menu.</summary>
    public static void AddItem(MenuFlyout menu, string text, string? glyph, Action click, bool enabled = true)
    {
        var item = new MenuFlyoutItem { Text = text, IsEnabled = enabled };
        if (glyph is not null) item.Icon = new FontIcon { Glyph = glyph };
        item.Click += (_, _) => click();
        menu.Items.Add(item);
    }
}
