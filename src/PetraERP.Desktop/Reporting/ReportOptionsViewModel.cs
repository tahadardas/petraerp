using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Reporting;

/// <summary>
/// The working copy edited by the report options window. Nothing is applied until the window asks the preference service to
/// save <see cref="Current"/>; the live sample in the window follows <see cref="Edited"/>.
/// </summary>
public sealed class ReportOptionsViewModel : ObservableObject
{
    private PreferenceOption<double> fontSize;
    private PreferenceOption<ReportRowDensity> density;
    private PreferenceOption<ReportGridLines> gridLines;
    private PreferenceOption<ReportParametersLayout> parametersLayout;
    private bool alternatingRows;

    public ReportOptionsViewModel(LocalizationService localization, ReportDisplayOptions initial)
    {
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(initial);
        Localization = localization;
        FontSizes = new[] { 0d, 11, 12, 13, 14, 16, 18 }
            .Select(size => new PreferenceOption<double>(size, size == 0 ? localization["REP_FontAuto"] : size.ToString(CultureInfo.InvariantCulture))).ToArray();
        Densities = Enum.GetValues<ReportRowDensity>().Select(value => new PreferenceOption<ReportRowDensity>(value, localization["REP_Density_" + value])).ToArray();
        GridLineModes = Enum.GetValues<ReportGridLines>().Select(value => new PreferenceOption<ReportGridLines>(value, localization["REP_Grid_" + value])).ToArray();
        Layouts = Enum.GetValues<ReportParametersLayout>().Select(value => new PreferenceOption<ReportParametersLayout>(value, localization["REP_Layout_" + value])).ToArray();
        fontSize = FontSizes[0];
        density = Densities[0];
        gridLines = GridLineModes[0];
        parametersLayout = Layouts[0];
        SetFrom(initial);
    }

    public LocalizationService Localization { get; }
    public IReadOnlyList<PreferenceOption<double>> FontSizes { get; }
    public IReadOnlyList<PreferenceOption<ReportRowDensity>> Densities { get; }
    public IReadOnlyList<PreferenceOption<ReportGridLines>> GridLineModes { get; }
    public IReadOnlyList<PreferenceOption<ReportParametersLayout>> Layouts { get; }

    /// <summary>Raised after any edit, so the live sample can redraw.</summary>
    public event EventHandler? Edited;

    public PreferenceOption<double> FontSize
    {
        get => fontSize;
        set { if (value is not null && SetProperty(ref fontSize, value)) Raise(); }
    }

    public PreferenceOption<ReportRowDensity> Density
    {
        get => density;
        set { if (value is not null && SetProperty(ref density, value)) Raise(); }
    }

    public PreferenceOption<ReportGridLines> GridLines
    {
        get => gridLines;
        set { if (value is not null && SetProperty(ref gridLines, value)) Raise(); }
    }

    public PreferenceOption<ReportParametersLayout> ParametersLayout
    {
        get => parametersLayout;
        set { if (value is not null && SetProperty(ref parametersLayout, value)) Raise(); }
    }

    public bool AlternatingRows
    {
        get => alternatingRows;
        set { if (SetProperty(ref alternatingRows, value)) Raise(); }
    }

    /// <summary>The options as currently edited.</summary>
    public ReportDisplayOptions Current => new()
    {
        FontSize = FontSize.Value,
        Density = Density.Value,
        GridLines = GridLines.Value,
        AlternatingRows = AlternatingRows,
        ParametersLayout = ParametersLayout.Value,
    };

    /// <summary>Replaces every edited value (used on opening and by Defaults).</summary>
    public void SetFrom(ReportDisplayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var value = options.Sanitized();
        FontSize = FontSizes.FirstOrDefault(option => Math.Abs(option.Value - value.FontSize) < 0.01) ?? FontSizes[0];
        Density = Densities.First(option => option.Value == value.Density);
        GridLines = GridLineModes.First(option => option.Value == value.GridLines);
        ParametersLayout = Layouts.First(option => option.Value == value.ParametersLayout);
        AlternatingRows = value.AlternatingRows;
    }

    private void Raise() => Edited?.Invoke(this, EventArgs.Empty);
}
