namespace Nova.Core.Layout;

public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public double IntersectionArea(RectD o)
    {
        var w = Math.Min(Right, o.Right) - Math.Max(X, o.X);
        var h = Math.Min(Bottom, o.Bottom) - Math.Max(Y, o.Y);
        return w > 0 && h > 0 ? w * h : 0;
    }
}

public readonly record struct MonitorInfo(string DeviceName, RectD WorkArea, bool IsPrimary);

/// <summary>Multi-monitor placement logic, independent of any UI framework.</summary>
public static class ScreenGeometry
{
    /// <summary>Minimum visible size (in the same units as the rectangles) for a window to count as reachable.</summary>
    public const double MinimumVisible = 80;

    /// <summary>
    /// Returns bounds that are guaranteed to be reachable: if the saved monitor still exists the window stays there;
    /// if it was disconnected or the resolution shrank, the window is moved (and if necessary resized) onto the best
    /// remaining monitor.
    /// </summary>
    public static (RectD Bounds, MonitorInfo Monitor) EnsureVisible(RectD saved, string? preferredDevice, IReadOnlyList<MonitorInfo> monitors)
    {
        if (monitors.Count == 0) throw new ArgumentException("At least one monitor is required.", nameof(monitors));
        var primary = monitors.FirstOrDefault(m => m.IsPrimary);
        if (primary.DeviceName == null) primary = monitors[0];

        if (double.IsNaN(saved.X) || double.IsNaN(saved.Y))
            return (CenterOn(primary.WorkArea, saved.Width, saved.Height), primary);

        // Monitor with the largest overlap, preferring the saved device on ties.
        var best = monitors
            .Select(m => (m, area: m.WorkArea.IntersectionArea(saved)))
            .OrderByDescending(t => t.area)
            .ThenByDescending(t => string.Equals(t.m.DeviceName, preferredDevice, StringComparison.OrdinalIgnoreCase))
            .First();

        var visibleW = Math.Min(saved.Right, best.m.WorkArea.Right) - Math.Max(saved.X, best.m.WorkArea.X);
        var visibleH = Math.Min(saved.Bottom, best.m.WorkArea.Bottom) - Math.Max(saved.Y, best.m.WorkArea.Y);
        if (best.area > 0 && visibleW >= MinimumVisible && visibleH >= MinimumVisible && saved.Y >= best.m.WorkArea.Y - 1)
            return (saved, best.m);

        var target = monitors.FirstOrDefault(m => string.Equals(m.DeviceName, preferredDevice, StringComparison.OrdinalIgnoreCase));
        if (target.DeviceName == null) target = best.area > 0 ? best.m : primary;
        return (ClampInto(saved, target.WorkArea), target);
    }

    public static RectD ClampInto(RectD r, RectD area)
    {
        var w = Math.Min(r.Width, area.Width);
        var h = Math.Min(r.Height, area.Height);
        var x = Math.Clamp(r.X, area.X, area.Right - w);
        var y = Math.Clamp(r.Y, area.Y, area.Bottom - h);
        return new RectD(x, y, w, h);
    }

    public static RectD CenterOn(RectD area, double width, double height)
    {
        var w = Math.Min(width, area.Width);
        var h = Math.Min(height, area.Height);
        return new RectD(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
    }
}
