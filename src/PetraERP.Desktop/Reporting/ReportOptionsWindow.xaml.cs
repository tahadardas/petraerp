using System.Windows;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Reporting;

/// <summary>
/// Report options: grid font, row height, grid lines, alternating rows and the layout of the report request window. Edits are a
/// working copy with a live sample; only Apply / OK saves them for the user. They never change a query, filter, total or balance.
/// </summary>
public partial class ReportOptionsWindow : Window
{
    private readonly UserPreferencesService preferences;
    private readonly ReportOptionsViewModel model;

    public ReportOptionsWindow(UserPreferencesService preferences, LocalizationService localization)
    {
        InitializeComponent();
        this.preferences = preferences;
        model = new ReportOptionsViewModel(localization, preferences.ReportOptions);
        DataContext = model;
        Sample.Columns[0].Header = localization["REP_Sample_Account"];
        Sample.Columns[1].Header = localization["REP_Sample_Debit"];
        Sample.Columns[2].Header = localization["REP_Sample_Credit"];
        Sample.ItemsSource = new[]
        {
            new SampleRow("1001 — " + localization["REP_Sample_Cash"], "1,250.00", "0.00"),
            new SampleRow("2101 — " + localization["REP_Sample_Supplier"], "0.00", "980.50"),
            new SampleRow("4001 — " + localization["REP_Sample_Sales"], "0.00", "269.50"),
            new SampleRow("5001 — " + localization["REP_Sample_Expense"], "410.00", "0.00"),
        };
        model.Edited += (_, _) => ReportGridAppearance.Apply(Sample, model.Current);
        ReportGridAppearance.Apply(Sample, model.Current);
    }

    /// <summary>A fixed sample line; the numbers are illustrative and are not data.</summary>
    public sealed record SampleRow(string Account, string Debit, string Credit);

    private void OnOk(object sender, RoutedEventArgs args)
    {
        preferences.SetReportOptions(model.Current);
        Close();
    }

    private void OnApply(object sender, RoutedEventArgs args) => preferences.SetReportOptions(model.Current);

    private void OnCancel(object sender, RoutedEventArgs args) => Close();

    private void OnDefault(object sender, RoutedEventArgs args) => model.SetFrom(new ReportDisplayOptions());
}
