using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace GameShelf.Views;

/// <summary>
/// A settings card that opens to show more (boards 19 and 21), like a Windows Settings expander. Unlike WinUI's
/// Expander its header is not one big button, so a switch or a button in it (Refresh, Import…, Stay ready) is its
/// own control for the keyboard and screen readers. The chevron opens and closes it; so does a click on the card
/// anywhere outside its controls. Closed, the body is collapsed and costs nothing.
/// </summary>
internal sealed class Disclosure
{
    private readonly Button _chevron;
    private readonly Border _header;
    private readonly FrameworkElement _body;
    private readonly string _name;

    public bool IsOpen { get; private set; }

    /// <summary>Raised after it opened or closed by the user.</summary>
    public event Action<bool>? Toggled;

    public Disclosure(Button chevron, Border header, FrameworkElement body, string name)
    {
        _chevron = chevron;
        _header = header;
        _body = body;
        _name = name;
        chevron.Click += (_, _) => Toggle();
        header.Tapped += OnHeaderTapped;
        Set(false);
    }

    public void Set(bool open)
    {
        IsOpen = open;
        _body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        _header.CornerRadius = open ? new CornerRadius(8, 8, 0, 0) : new CornerRadius(8);
        var margin = _header.Margin;
        _header.Margin = new Thickness(margin.Left, margin.Top, margin.Right, open ? 0 : 6);
        _chevron.Content = open ? "" : "";
        var label = Strings.Format(open ? Strings.DisclosureHide : Strings.DisclosureShow, _name);
        AutomationProperties.SetName(_chevron, label);
        ToolTipService.SetToolTip(_chevron, label);
        if (FrameworkElementAutomationPeer.FromElement(_chevron) is { } peer)
            peer.RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, "", label);
    }

    private void Toggle()
    {
        Set(!IsOpen);
        Toggled?.Invoke(IsOpen);
        if (IsOpen) RevealBody();
    }

    /// <summary>
    /// Scrolls just enough to show what opened, and only if it's cut off at the bottom; never so far that the card's
    /// own header leaves the top. Measured after layout, so the page doesn't jump to a guess first.
    /// </summary>
    private void RevealBody()
    {
        ScrollViewer? scroller = null;
        for (var node = VisualTreeHelper.GetParent(_header); node is not null && scroller is null; node = VisualTreeHelper.GetParent(node))
            scroller = node as ScrollViewer;
        if (scroller is null) return;
        scroller.UpdateLayout();
        const double gap = 12;
        double headerTop = _header.TransformToVisual(scroller).TransformPoint(default).Y;
        double bodyBottom = _body.TransformToVisual(scroller).TransformPoint(new Windows.Foundation.Point(0, _body.ActualHeight)).Y;
        double needed = bodyBottom + gap - scroller.ViewportHeight;
        if (needed <= 0) return; // all of it shows already
        double delta = Math.Min(needed, headerTop - gap);
        if (delta <= 0) return;
        bool animate = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
        scroller.ChangeView(null, scroller.VerticalOffset + delta, null, disableAnimation: !animate);
    }

    /// <summary>A click on the card itself, not on one of its controls.</summary>
    private void OnHeaderTapped(object sender, TappedRoutedEventArgs e)
    {
        for (var node = e.OriginalSource as DependencyObject; node is not null && node != _header; node = VisualTreeHelper.GetParent(node))
            if (node is ButtonBase or ToggleSwitch or ComboBox or Selector or TextBox or HyperlinkButton) return;
        Toggle();
    }
}
