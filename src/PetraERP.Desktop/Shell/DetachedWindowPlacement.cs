using System.Text.Json.Serialization;

namespace PetraERP.Desktop.Shell;

/// <summary>Only window geometry: monitor-relative offsets and size in DIPs, normal/maximized state. No content.</summary>
public sealed record DetachedWindowPlacement(string Monitor, double OffsetX, double OffsetY, double Width, double Height, bool Maximized)
{
    [JsonIgnore]
    public bool IsValid => Monitor is { Length: > 0 and <= 32 }
        && double.IsFinite(OffsetX) && Math.Abs(OffsetX) <= 100000
        && double.IsFinite(OffsetY) && Math.Abs(OffsetY) <= 100000
        && double.IsFinite(Width) && Width is >= 64 and <= 16384
        && double.IsFinite(Height) && Height is >= 64 and <= 16384;
}

/// <summary>Native virtual-screen coordinates, including negative monitor origins; work area excludes taskbars.</summary>
internal sealed record MonitorWorkArea(string Device, int Left, int Top, int Width, int Height);
internal sealed record WindowPixelBounds(int Left, int Top, int Width, int Height);

internal static class WindowPlacementGeometry
{
    internal static WindowPixelBounds Fit(DetachedWindowPlacement? saved, MonitorWorkArea monitor, double scale,
        double defaultWidth, double defaultHeight, double minimumWidth, double minimumHeight)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        if (monitor.Width <= 0 || monitor.Height <= 0 || !double.IsFinite(scale) || scale <= 0)
            throw new ArgumentException("A current work area and DPI scale are required.");
        var valid = saved is { IsValid: true };
        var width = (int)Math.Round(Math.Min(monitor.Width, Math.Max(minimumWidth, valid ? saved!.Width : defaultWidth) * scale));
        var height = (int)Math.Round(Math.Min(monitor.Height, Math.Max(minimumHeight, valid ? saved!.Height : defaultHeight) * scale));
        var x = valid ? saved!.OffsetX * scale : Math.Max(0, (monitor.Width - width) / 2.0);
        var y = valid ? saved!.OffsetY * scale : Math.Max(0, (monitor.Height - height) / 2.0);
        return new(monitor.Left + (int)Math.Round(Math.Clamp(x, 0, monitor.Width - width)),
            monitor.Top + (int)Math.Round(Math.Clamp(y, 0, monitor.Height - height)), width, height);
    }
}
