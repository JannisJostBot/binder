using GameShelf.Models;

namespace GameShelf.Services;

/// <summary>A rectangle in physical screen pixels.</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
}

/// <summary>Thickness per edge in pixels, e.g. of auto-hide taskbars.</summary>
public readonly record struct PixelEdges(int Left, int Top, int Right, int Bottom);

/// <summary>
/// Where the panel goes on a monitor. Pure math in physical pixels, so it's testable and independent of WPF's
/// DIP coordinates (which get confusing across monitors with different scaling).
/// </summary>
public static class Placement
{
    public const double PanelWidth = 560;
    public const double EdgeMargin = 28;
    /// <summary>The flyout sits closer to the screen edge (spec §5).</summary>
    public const double FlyoutMargin = 12;
    public const double MinHeight = 400;

    /// <param name="workArea">The monitor's work area (excludes taskbars that reserve space).</param>
    /// <param name="autoHide">
    /// Auto-hide taskbars reserve no space, so they'd slide over the panel; their thickness is kept free too.
    /// </param>
    /// <param name="scale">The monitor's DPI scale (1.0 = 100 %).</param>
    /// <param name="free">The saved top-left corner in Free position.</param>
    public static PixelRect Compute(PixelRect workArea, PixelEdges autoHide, double scale, DockPosition position,
        (int X, int Y)? free, double edgeMargin = EdgeMargin)
    {
        var area = new PixelRect(
            workArea.Left + autoHide.Left, workArea.Top + autoHide.Top,
            workArea.Right - autoHide.Right, workArea.Bottom - autoHide.Bottom);

        int width = Math.Min((int)Math.Round(PanelWidth * scale), area.Width);
        int margin = (int)Math.Round(edgeMargin * scale);
        int height = Math.Min(area.Height, Math.Max((int)Math.Round(MinHeight * scale), area.Height - 2 * margin));

        int left = position switch
        {
            DockPosition.Left => area.Left + margin,
            DockPosition.Free when free is { } f => f.X,
            _ => area.Right - width - margin,
        };
        int top = position == DockPosition.Free && free is { } p ? p.Y : area.Top + margin;

        // Keep the whole panel on this monitor's usable area (also for a free position saved elsewhere).
        left = Math.Clamp(left, area.Left, Math.Max(area.Left, area.Right - width));
        top = Math.Clamp(top, area.Top, Math.Max(area.Top, area.Bottom - height));
        return new PixelRect(left, top, left + width, top + height);
    }
}
