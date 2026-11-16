using System.Windows;
using System.Windows.Controls;

namespace PetraERP.Desktop.Reporting;

/// <summary>How the report-request (parameters) window arranges its panels.</summary>
public enum ReportParametersLayout
{
    /// <summary>Three columns on wide windows, tabs on narrow ones (the original behavior).</summary>
    Automatic = 0,
    /// <summary>Always tabs: parameters, options, sources.</summary>
    Tabs = 1,
    /// <summary>Always side-by-side columns.</summary>
    Columns = 2,
}

/// <summary>Row height of report result grids.</summary>
public enum ReportRowDensity
{
    /// <summary>The height set by the program density (Standard / Dense).</summary>
    Standard = 0,
    Compact = 1,
    Comfortable = 2,
}

/// <summary>Which grid lines report result grids draw.</summary>
public enum ReportGridLines
{
    None = 0,
    Horizontal = 1,
    Vertical = 2,
    Both = 3,
}

/// <summary>
/// Presentation-only report options of a user on this computer. They change how results look and how the request window is
/// arranged; they never touch a query, a filter, a total or a balance. Nothing financial is stored here.
/// </summary>
public sealed record ReportDisplayOptions
{
    public const double MinimumFontSize = 10;
    public const double MaximumFontSize = 20;

    /// <summary>0 keeps the theme's font size; otherwise 10..20.</summary>
    public double FontSize { get; init; }
    public ReportRowDensity Density { get; init; } = ReportRowDensity.Standard;
    public ReportGridLines GridLines { get; init; } = ReportGridLines.Both;
    public bool AlternatingRows { get; init; }
    public ReportParametersLayout ParametersLayout { get; init; } = ReportParametersLayout.Automatic;

    /// <summary>The same options with every value forced into its allowed range (a hand-edited file cannot break the grid).</summary>
    public ReportDisplayOptions Sanitized() => this with
    {
        FontSize = !double.IsFinite(FontSize) || FontSize <= 0 ? 0 : Math.Clamp(FontSize, MinimumFontSize, MaximumFontSize),
        Density = Enum.IsDefined(Density) ? Density : ReportRowDensity.Standard,
        GridLines = Enum.IsDefined(GridLines) ? GridLines : ReportGridLines.Both,
        ParametersLayout = Enum.IsDefined(ParametersLayout) ? ParametersLayout : ReportParametersLayout.Automatic,
    };
}

/// <summary>
/// The report options in force for the signed-in user. The preference service publishes them after sign-in and after every
/// change; report grids and request windows read them while they are loaded and follow <see cref="Changed"/>.
/// </summary>
public static class ReportDisplayOptionsHub
{
    private static ReportDisplayOptions current = new();

    public static ReportDisplayOptions Current => current;

    public static event EventHandler? Changed;

    public static void Set(ReportDisplayOptions? options)
    {
        var value = (options ?? new ReportDisplayOptions()).Sanitized();
        if (value == current) return;
        current = value;
        Changed?.Invoke(null, EventArgs.Empty);
    }
}

/// <summary>Applies <see cref="ReportDisplayOptions"/> to a result grid (shared by the viewer and the live sample in the options window).</summary>
public static class ReportGridAppearance
{
    public const double CompactRowHeight = 22;
    public const double ComfortableRowHeight = 34;

    public static void Apply(DataGrid grid, ReportDisplayOptions options)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(options);
        if (options.FontSize > 0) grid.FontSize = options.FontSize; else grid.ClearValue(Control.FontSizeProperty);
        grid.GridLinesVisibility = options.GridLines switch
        {
            ReportGridLines.None => DataGridGridLinesVisibility.None,
            ReportGridLines.Horizontal => DataGridGridLinesVisibility.Horizontal,
            ReportGridLines.Vertical => DataGridGridLinesVisibility.Vertical,
            _ => DataGridGridLinesVisibility.All,
        };
        grid.AlternationCount = options.AlternatingRows ? 2 : 0;
        switch (options.Density)
        {
            case ReportRowDensity.Compact: grid.RowHeight = CompactRowHeight; break;
            case ReportRowDensity.Comfortable: grid.RowHeight = ComfortableRowHeight; break;
            default: grid.ClearValue(DataGrid.RowHeightProperty); break;
        }
    }
}
