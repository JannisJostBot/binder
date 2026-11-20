using Binder.Services;
using Binder.Views;
using Microsoft.UI.Xaml;
using ExpandCollapseState = Microsoft.UI.Xaml.Automation.ExpandCollapseState;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Binder;

/// <summary>Keyboard and controller: Esc and back, the gamepad (spec §4, board 3) and type-to-search.</summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// Esc that no control used: back from a page to the shelf, else close the flyout. The search box clears
    /// itself first (see ShelfView).
    /// </summary>
    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape && GoBack()) e.Handled = true;
    }

    /// <summary>Esc or a controller's B: back from a page to the shelf, else close the flyout. False if nothing to do.</summary>
    private bool GoBack()
    {
        if (Detail.Visibility == Visibility.Visible || SettingsPage.Visibility == Visibility.Visible) ShowShelf();
        else if (FreeSpacePage.Visibility == Visibility.Visible) LeaveFreeSpace();
        else if (GameModePage.Visibility == Visibility.Visible) GameModeBack();
        else if (YearReviewPage.Visibility == Visibility.Visible) ShowShelf();
        else if (ImportPage.Visibility == Visibility.Visible) ImportPage.CancelByEscape();
        else if (IsFlyout) App.Current.ClosePanel(this);
        else return false;
        return true;
    }

    // ---- Controller (spec §4, board 3) ----

    private readonly Gamepad _pad;
    private bool _padMode;
    private Windows.Foundation.Point? _padPointer;

    /// <summary>A controller was used: show its hints and the bold focus ring (until the mouse or keyboard is used).</summary>
    private void EnterPadMode()
    {
        if (_padMode) return;
        _padMode = true;
        _padPointer = null;
        Shelf.SetControllerMode(true);
    }

    private void LeavePadMode()
    {
        if (!_padMode) return;
        _padMode = false;
        Shelf.SetControllerMode(false);
    }

    // Not the Gamepad* keys, in case Windows also reports the pad as keys.
    private void OnAnyKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is < Windows.System.VirtualKey.GamepadA or > Windows.System.VirtualKey.GamepadRightThumbstickLeft) LeavePadMode();
    }

    private void OnAnyPointerPressed(object sender, PointerRoutedEventArgs e) => LeavePadMode();

    /// <summary>A real mouse move (a few pixels, not the jitter of a hand resting on it) leaves controller mode.</summary>
    private void OnAnyPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_padMode) return;
        var at = e.GetCurrentPoint(Root).Position;
        if (_padPointer is not { } first) _padPointer = at;
        else if (Math.Abs(at.X - first.X) + Math.Abs(at.Y - first.Y) > 8) LeavePadMode();
    }

    private object? Focused => FocusManager.GetFocusedElement(Root.XamlRoot);

    /// <summary>The open menu, drop-down or dialog, if any (tooltips don't count): input goes there first.</summary>
    private Popup? OpenPopup()
    {
        var popups = VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot);
        for (int i = popups.Count - 1; i >= 0; i--)
            if (popups[i].Child is not ToolTip and not null) return popups[i];
        return null;
    }

    private void OnPadMoved(FocusNavigationDirection direction)
    {
        EnterPadMode();
        if (OpenPopup() is { } popup)
        {
            FocusManager.TryMoveFocus(direction, new FindNextElementOptions { SearchRoot = popup.Child });
            return;
        }
        var focused = Focused as DependencyObject;
        if (Shelf.Visibility == Visibility.Visible && Shelf.MoveFocus(focused, direction)) return;
        // Nothing focused yet: start at the first control.
        var options = new FindNextElementOptions { SearchRoot = Root };
        FocusManager.TryMoveFocus(focused is null ? FocusNavigationDirection.Next : direction, options);
    }

    private void OnPadPressed(PadButton button)
    {
        EnterPadMode();
        var popup = OpenPopup();
        var focused = Focused as DependencyObject;
        var game = popup is null && Shelf.Visibility == Visibility.Visible ? Shelf.GameAt(focused) : null;
        switch (button)
        {
            case PadButton.A when game is not null:
                Play(game);
                break;
            case PadButton.A:
                if (focused is UIElement element) PadInvoke(element);
                break;
            case PadButton.B when popup is not null:
                if (popup.Child is ContentDialog dialog) dialog.Hide(); // as Cancel
                else popup.IsOpen = false;
                break;
            case PadButton.B:
                GoBack();
                break;
            case PadButton.X when game is not null:
                ShowDetail(game);
                break;
            case PadButton.Y when game is not null:
                OnGameAction(game, GameAction.ToggleFavorite);
                Shelf.FocusGame(game, FocusState.Keyboard); // favorites move to the front: follow it
                break;
            case PadButton.Y when popup is null && Detail.Visibility == Visibility.Visible && Detail.Game is { } shown:
                OnGameAction(shown, GameAction.ToggleFavorite);
                break;
            case PadButton.LeftShoulder or PadButton.RightShoulder when popup is null && YearReviewPage.Visibility == Visibility.Visible:
                // Year in review: the card before or the next one (spec §28).
                var review = YearReviewPage.ViewModel!;
                (button == PadButton.RightShoulder ? review.NextCommand : review.BackCommand).Execute(null);
                break;
            case PadButton.LeftShoulder or PadButton.RightShoulder when popup is null && Shelf.Visibility == Visibility.Visible:
                bool inGrid = game is not null;
                Shelf.ViewModel!.CycleFilter(button == PadButton.RightShoulder ? 1 : -1);
                if (inGrid) Shelf.FocusFirstGame();
                break;
            case PadButton.Menu when popup is null && SetupPage.Visibility != Visibility.Visible:
                if (SettingsPage.Visibility == Visibility.Visible) ShowShelf();
                else ShowSettings();
                break;
        }
    }

    /// <summary>
    /// A on anything but a cover: what Enter or Space would do, through the control's UI Automation pattern
    /// (press a button, flip a switch or check box, pick an item, open a drop-down).
    /// </summary>
    private static void PadInvoke(UIElement element)
    {
        var peer = FrameworkElementAutomationPeer.FromElement(element) ?? FrameworkElementAutomationPeer.CreatePeerForElement(element);
        if (peer is null) return;
        if (peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke) invoke.Invoke();
        else if (peer.GetPattern(PatternInterface.Toggle) is IToggleProvider toggle) toggle.Toggle();
        else if (peer.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider item)
        {
            item.Select();
            // A drop-down's item: picking it closes the list, as a click would.
            if (element is ComboBoxItem comboItem && ItemsControl.ItemsControlFromItemContainer(comboItem) is ComboBox combo)
                combo.IsDropDownOpen = false;
        }
        else if (peer.GetPattern(PatternInterface.ExpandCollapse) is IExpandCollapseProvider expand)
        {
            if (expand.ExpandCollapseState == ExpandCollapseState.Collapsed) expand.Expand();
            else expand.Collapse();
        }
    }

    /// <summary>Right stick: scrolls what has focus (or the page's grid) without moving focus.</summary>
    private void OnPadScrolled(double pixels)
    {
        EnterPadMode();
        if (OpenPopup() is not null) return;
        ScrollViewer? scroller = null;
        for (var at = Focused as DependencyObject; at is not null && scroller is null; at = VisualTreeHelper.GetParent(at))
            if (at is ScrollViewer { ScrollableHeight: > 0 } found) scroller = found;
        if (scroller is null && Shelf.Visibility == Visibility.Visible) scroller = Shelf.GridScroller;
        scroller?.ChangeView(null, Math.Clamp(scroller.VerticalOffset + pixels, 0, scroller.ScrollableHeight), null, disableAnimation: true);
    }

    /// <summary>Type-to-search: printable keys typed anywhere outside a text box go to the search box.</summary>
    private void OnCharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs e)
    {
        // No shelf to search behind the setup; on Free up space a stray key mustn't throw the plan away.
        // Year in review: Space is its Next, and a stray key mustn't leave the story for the search.
        if (e.Handled || SetupPage.Visibility == Visibility.Visible || FreeSpacePage.Visibility == Visibility.Visible ||
            YearReviewPage.Visibility == Visibility.Visible) return;
        var focused = FocusManager.GetFocusedElement(Root.XamlRoot);
        if (focused is TextBox || char.IsControl(e.Character)) return;
        // Space activates a focused button, item or switch; leave it alone.
        if (e.Character == ' ' && focused is ButtonBase or SelectorItem or ToggleSwitch) return;
        ShowShelf(); // typing on the detail view goes back to the grid and searches
        Shelf.TypeIntoSearch(e.Character.ToString());
        e.Handled = true;
    }
}
