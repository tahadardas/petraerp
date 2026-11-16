using System.Data;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace PetraERP.Desktop.Management;

public partial class AnalyticalReportView : UserControl
{
    public AnalyticalReportView() => InitializeComponent();

    private void ColumnGenerating(object? sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (ResultGrid.ItemsSource is System.Data.DataView view && view.Table?.Columns[e.PropertyName] is { } column) e.Column.Header = column.Caption;
    }

    private void ExportClicked(object sender, RoutedEventArgs args)
    {
        if (DataContext is not AnalyticalReportViewModel { Table: not null } model) return;
        var dialog = new SaveFileDialog { Filter = "CSV|*.csv|Excel|*.xlsx|PDF|*.pdf", FileName = model.SelectedReport?.Id ?? "report" };
        if (dialog.ShowDialog() != true) return;
        var extension = Path.GetExtension(dialog.FileName).ToUpperInvariant();
        if (extension is ".XLSX" or ".PDF")
        {
            var document = model.BuildDocument();
            if (document is null) return;
            File.WriteAllBytes(dialog.FileName, extension == ".XLSX"
                ? new PetraERP.Reporting.OpenSource.ExcelReportExporter().Export(document)
                : new PetraERP.Reporting.OpenSource.FastReportPdfExporter().Export(document));
        }
        else File.WriteAllText(dialog.FileName, model.BuildCsv(), new UTF8Encoding(true));
    }
}
