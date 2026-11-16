namespace PetraERP.Desktop.Shell;

/// <summary>A rectangle in the coordinate space of the MDI container (DIPs, origin at the container's top-left).</summary>
public readonly record struct MdiRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;

    /// <summary>True for finite numbers and a positive size; anything else is rejected before it can reach layout.</summary>
    public bool IsValid => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Width) && double.IsFinite(Height) && Width > 0 && Height > 0;
}

/// <summary>State of one internal MDI window.</summary>
public enum MdiWindowState
{
    Normal = 0,
    Minimized = 1,
    Maximized = 2,
}

/// <summary>
/// Pure geometry of the internal (classic MDI) workspace. Every result is computed from the bounds of the MDI container
/// itself, never from <c>SystemParameters.WorkArea</c> or any screen coordinate, so tiling and cascading cannot push an
/// internal window out of the main window and cannot cut the menu or toolbar. It holds no UI, so it is unit-testable.
/// </summary>
public static class MdiLayoutEngine
{
    public const double MinimumWidth = 280;
    public const double MinimumHeight = 160;
    public const double TitleHeight = 28;
    public const double CascadeStep = 28;
    /// <summary>A minimized window is its title bar plus the 1-DIP border on both sides.</summary>
    public const double MinimizedHeight = TitleHeight + 2;
    public const double MinimizedWidth = 190;
    public const double MinimizedGap = 4;

    /// <summary>Windows overlapping like a classic "Cascade": same size, each one a step down and inwards, wrapping when the container is full.</summary>
    public static IReadOnlyList<MdiRect> Cascade(int count, MdiRect bounds)
    {
        if (count <= 0 || !bounds.IsValid) return [];
        var width = Math.Min(bounds.Width, Math.Max(MinimumWidth, bounds.Width * 0.72));
        var height = Math.Min(bounds.Height, Math.Max(MinimumHeight, bounds.Height * 0.72));
        var room = Math.Min(bounds.Width - width, bounds.Height - height);
        var slots = Math.Max(1, (int)Math.Floor(room / CascadeStep) + 1);
        var result = new MdiRect[count];
        for (var index = 0; index < count; index++)
        {
            var offset = (index % slots) * CascadeStep;
            result[index] = new MdiRect(bounds.X + offset, bounds.Y + offset, width, height);
        }

        return result;
    }

    /// <summary>"Tile horizontally" as in the classic menu: windows stacked as full-width rows. Falls back to a grid when rows would be too short.</summary>
    public static IReadOnlyList<MdiRect> TileHorizontal(int count, MdiRect bounds)
    {
        if (count <= 0 || !bounds.IsValid) return [];
        var height = bounds.Height / count;
        if (height < MinimumHeight && count > 1) return Grid(count, bounds);
        var result = new MdiRect[count];
        for (var index = 0; index < count; index++) result[index] = new MdiRect(bounds.X, bounds.Y + (index * height), bounds.Width, height);
        return result;
    }

    /// <summary>"Tile vertically": windows side by side as full-height columns. Falls back to a grid when columns would be too narrow.</summary>
    public static IReadOnlyList<MdiRect> TileVertical(int count, MdiRect bounds)
    {
        if (count <= 0 || !bounds.IsValid) return [];
        var width = bounds.Width / count;
        if (width < MinimumWidth && count > 1) return Grid(count, bounds);
        var result = new MdiRect[count];
        for (var index = 0; index < count; index++) result[index] = new MdiRect(bounds.X + (index * width), bounds.Y, width, bounds.Height);
        return result;
    }

    /// <summary>Near-square grid used when a plain row or column tiling would make windows unusably small.</summary>
    public static IReadOnlyList<MdiRect> Grid(int count, MdiRect bounds)
    {
        if (count <= 0 || !bounds.IsValid) return [];
        var columns = (int)Math.Ceiling(Math.Sqrt(count));
        var rows = (int)Math.Ceiling(count / (double)columns);
        var width = bounds.Width / columns;
        var height = bounds.Height / rows;
        var result = new MdiRect[count];
        for (var index = 0; index < count; index++)
            result[index] = new MdiRect(bounds.X + ((index % columns) * width), bounds.Y + ((index / columns) * height), width, height);
        return result;
    }

    /// <summary>
    /// Keeps a window entirely inside the container: size is limited to the container, raised to the minimum when it fits, and
    /// the position is moved back inside. A non-finite rectangle is replaced by a default-sized one at the origin.
    /// </summary>
    public static MdiRect Clamp(MdiRect rect, MdiRect bounds)
    {
        if (!bounds.IsValid) return rect;
        var width = double.IsFinite(rect.Width) && rect.Width > 0 ? rect.Width : Math.Min(bounds.Width, 640);
        var height = double.IsFinite(rect.Height) && rect.Height > 0 ? rect.Height : Math.Min(bounds.Height, 420);
        width = Math.Min(bounds.Width, Math.Max(Math.Min(MinimumWidth, bounds.Width), width));
        height = Math.Min(bounds.Height, Math.Max(Math.Min(MinimumHeight, bounds.Height), height));
        var x = double.IsFinite(rect.X) ? rect.X : bounds.X;
        var y = double.IsFinite(rect.Y) ? rect.Y : bounds.Y;
        x = Math.Clamp(x, bounds.X, Math.Max(bounds.X, bounds.Right - width));
        y = Math.Clamp(y, bounds.Y, Math.Max(bounds.Y, bounds.Bottom - height));
        return new MdiRect(x, y, width, height);
    }

    /// <summary>
    /// Where the <paramref name="index"/>-th minimized window sits: title-bar sized chips along the bottom edge, starting at the
    /// reading side (right in RTL) and wrapping upwards when the row is full.
    /// </summary>
    public static MdiRect MinimizedSlot(int index, MdiRect bounds, bool rightToLeft)
    {
        if (!bounds.IsValid || index < 0) return new MdiRect(0, 0, MinimizedWidth, MinimizedHeight);
        var perRow = Math.Max(1, (int)Math.Floor((bounds.Width + MinimizedGap) / (MinimizedWidth + MinimizedGap)));
        var column = index % perRow;
        var row = index / perRow;
        var x = rightToLeft
            ? bounds.Right - MinimizedWidth - (column * (MinimizedWidth + MinimizedGap))
            : bounds.X + (column * (MinimizedWidth + MinimizedGap));
        var y = bounds.Bottom - MinimizedHeight - (row * (MinimizedHeight + MinimizedGap));
        return new MdiRect(Math.Max(bounds.X, x), Math.Max(bounds.Y, y), Math.Min(MinimizedWidth, bounds.Width), MinimizedHeight);
    }

    /// <summary>Index of the next (+1) or previous (-1) window, wrapping around; -1 when there is nothing to activate.</summary>
    public static int Neighbor(int index, int count, int direction)
    {
        if (count <= 0) return -1;
        if (index < 0 || index >= count) return direction >= 0 ? 0 : count - 1;
        return (((index + direction) % count) + count) % count;
    }
}
