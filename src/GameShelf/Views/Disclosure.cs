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
        var label = (open ? "Hide " : "Show ") + _name;
        AutomationProperties.SetName(_chevron, label);
        ToolTipService.SetToolTip(_chevron, label);
        if (FrameworkElementAutomationPeer.FromElement(_chevron) is { } peer)
            peer.RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, "", label);
    }

    private void Toggle()
    {
        Set(!IsOpen);
        Toggled?.Invoke(IsOpen);
        if (IsOpen) _body.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 1, AnimationDesired = false });
    }

    /// <summary>A click on the card itself, not on one of its controls.</summary>
    private void OnHeaderTapped(object sender, TappedRoutedEventArgs e)
    {
        for (var node = e.OriginalSource as DependencyObject; node is not null && node != _header; node = VisualTreeHelper.GetParent(node))
            if (node is ButtonBase or ToggleSwitch or ComboBox or Selector or TextBox or HyperlinkButton) return;
        Toggle();
    }
}
