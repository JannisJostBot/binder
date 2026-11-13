using Binder.Library;
using Binder.Models;
using Binder.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Foundation;

namespace Binder.Views;

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
        var usedKeys = new HashSet<char>();
        void Add(string text, string glyph, GameAction action, bool enabled = true)
        {
            var item = new MenuFlyoutItem
            {
                Text = text,
                Icon = new FontIcon { Glyph = glyph },
                IsEnabled = enabled,
                AccessKey = AccessKey(text, usedKeys),
            };
            item.Click += (_, _) => onAction(action);
            menu.Items.Add(item);
        }

        if (includePlayAndDetails)
        {
            Add(Strings.Play, "", GameAction.Play);
            Add(Strings.Details, "", GameAction.Details);
            menu.Items.Add(new MenuFlyoutSeparator());
        }
        Add(Strings.OpenInstallFolder, "", GameAction.OpenFolder, enabled: GameActions.InstallFolder(game) is not null);
        if (GameActions.StoreUrl(game) is not null) Add(Strings.StorePage, "", GameAction.StorePage);
        if (game.Source == GameSource.Steam)
        {
            // Steam's own dialogs handle these (Uninstall asks for confirmation there).
            Add(Strings.VerifyFiles, "", GameAction.VerifyFiles);
            Add(Strings.UninstallInSteam, "", GameAction.Uninstall);
        }
        // The detail view's "…" menu: plan room for a new game on this game's drive (spec §12).
        if (!includePlayAndDetails && SpacePlanner.DriveOf(game) is not null)
            Add(Strings.FreeUpSpaceMenu, "", GameAction.FreeUpSpace);
        menu.Items.Add(new MenuFlyoutSeparator());
        Add(game.Favorite ? Strings.RemoveFavorite : Strings.AddFavorite, game.Favorite ? "" : "",
            GameAction.ToggleFavorite);
        Add(Strings.HideFromShelf, "", GameAction.Hide);
        return menu;
    }

    /// <summary>The first letter of the label that no earlier item uses, so access keys work in every language.</summary>
    private static string AccessKey(string text, HashSet<char> used)
    {
        foreach (var c in text)
            if (char.IsLetter(c) && used.Add(char.ToUpperInvariant(c))) return char.ToUpperInvariant(c).ToString();
        return "";
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
