using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GameShelf.Views;

/// <summary>One bar: its axis label, its tooltip and its value.</summary>
public readonly record struct Bar(string Label, string ToolTip, double Value);

/// <summary>
/// A single-series bar chart in the accent color: thin bars with 4 px rounded tops on a recessive baseline,
/// a tooltip per bar and one automation name that reads out every value. Built once per Show; static after
/// that (no animation, nothing running while idle).
/// </summary>
public sealed partial class BarChart : Grid
{
    /// <summary>Height of the tallest bar, in DIPs.</summary>
    public double PlotHeight { get; set; } = 48;
    public double BarWidth { get; set; } = 8;
    public bool ShowLabels { get; set; } = true;

    public BarChart()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    }

    /// <param name="description">What the chart shows, e.g. "Playtime this week"; the values are added to it for screen readers.</param>
    public void Show(IReadOnlyList<Bar> bars, string description)
    {
        Children.Clear();
        ColumnDefinitions.Clear();
        double max = bars.Count == 0 ? 0 : bars.Max(b => b.Value);
        var accent = Resource("AccentFillColorDefaultBrush");
        var baseline = new Border
        {
            Height = 1,
            Background = Resource("DividerStrokeColorDefaultBrush"),
        };

        for (int i = 0; i < bars.Count; i++)
        {
            var bar = bars[i];
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // The column's full height takes the hover, so a tooltip shows even over a short or empty bar.
            var slot = new Grid { Height = PlotHeight, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
            double height = max <= 0 || bar.Value <= 0 ? 0 : Math.Max(2, Math.Round(bar.Value / max * PlotHeight));
            if (height > 0)
            {
                slot.Children.Add(new Border
                {
                    Width = BarWidth,
                    Height = height,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    CornerRadius = new CornerRadius(Math.Min(4, BarWidth / 2), Math.Min(4, BarWidth / 2), 0, 0),
                    Background = accent,
                });
            }
            ToolTipService.SetToolTip(slot, bar.ToolTip);
            SetColumn(slot, i);
            Children.Add(slot);

            if (ShowLabels)
            {
                var label = new TextBlock
                {
                    Text = bar.Label,
                    Margin = new Thickness(0, 4, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    FontSize = 11,
                    Foreground = Resource("TextFillColorTertiaryBrush"),
                };
                SetRow(label, 2);
                SetColumn(label, i);
                Children.Add(label);
            }
        }

        SetRow(baseline, 1);
        SetColumnSpan(baseline, Math.Max(1, bars.Count));
        Children.Add(baseline);

        // Screen readers get the numbers as text; the shapes themselves are decoration.
        AutomationProperties.SetName(this, description + ": " + string.Join(", ", bars.Select(b => b.ToolTip)));
        foreach (var child in Children) AutomationProperties.SetAccessibilityView(child, AccessibilityView.Raw);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ChartPeer(this);

    private static Brush Resource(string key) => (Brush)Application.Current.Resources[key];

    /// <summary>Makes the chart one readable element (an image with a name) instead of an anonymous panel.</summary>
    private sealed partial class ChartPeer(BarChart owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;
        protected override bool IsControlElementCore() => true;
    }
}
