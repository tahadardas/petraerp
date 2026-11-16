using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ClosedXML.Excel;
using Microsoft.Win32;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.DocumentProcessing;

/// <summary>
/// Export and print are view concerns (file/print dialogs), so they live here and read the persisted rows from the
/// view-model. Every exported cell is the same value the operator reviewed on screen.
/// </summary>
public partial class PostingManagementView : UserControl
{
    private static readonly string[] Headers =
    [
        "النوع", "الرقم", "التاريخ", "الجهة / الحساب", "المبلغ والعملة", "الحالة السابقة", "الحالة الجديدة",
        "القيد", "رقم الترحيل", "النتيجة", "التصنيف", "السبب", "الرمز", "المحاولات",
    ];

    private bool exporting;

    public PostingManagementView() => InitializeComponent();

    private PostingManagementViewModel? Model => DataContext as PostingManagementViewModel;

    private static string[] Row(PostingResultRow row) =>
    [
        row.TypeDisplay, row.NumberDisplay, row.DateDisplay, row.PartyDisplay, row.AmountDisplay,
        row.PreviousStatus ?? string.Empty, row.NewStatus ?? string.Empty, row.JournalDisplay,
        row.PostingNumberDisplay, row.StatusDisplay, row.ClassificationDisplay, row.Reason,
        row.FailureCode ?? string.Empty, row.AttemptCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
    ];

    private async void ExportClicked(object sender, RoutedEventArgs args)
    {
        if (exporting || Model is not { } model || model.Results.Count == 0) return;
        var dialog = new SaveFileDialog
        {
            Title = "تصدير نتائج الترحيل الجماعي",
            FileName = "posting-batch-" + (model.LastBatchId?.ToString("N")[..8] ?? "results"),
            DefaultExt = ".xlsx",
            Filter = "Excel|*.xlsx",
            AddExtension = true,
            OverwritePrompt = true,
            CheckPathExists = true,
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        exporting = true;
        var temporary = dialog.FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var rows = model.Results.Select(Row).ToList();
            var header = model.Localization["D_PostingManagement"] + " · " + model.TotalsText;
            var content = await Task.Run(() => BuildWorkbook(header, rows)).ConfigureAwait(true);
            await File.WriteAllBytesAsync(temporary, content).ConfigureAwait(true);
            File.Move(temporary, dialog.FileName, true);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            model.Operation.Report(exception, OperationKind.Read);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            exporting = false;
        }
    }

    private static byte[] BuildWorkbook(string header, List<string[]> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("نتائج الترحيل");
        sheet.RightToLeft = true;
        sheet.Cell(1, 1).Value = header;
        sheet.Range(1, 1, 1, Headers.Length).Merge().Style.Font.SetBold().Font.SetFontSize(12);
        for (var column = 0; column < Headers.Length; column++) sheet.Cell(2, column + 1).Value = Headers[column];
        var head = sheet.Range(2, 1, 2, Headers.Length);
        head.Style.Font.SetBold();
        head.Style.Fill.SetBackgroundColor(XLColor.FromArgb(0xEE, 0xEE, 0xEE));
        head.Style.Border.SetBottomBorder(XLBorderStyleValues.Thin);

        for (var index = 0; index < rows.Count; index++)
            for (var column = 0; column < rows[index].Length; column++)
                sheet.Cell(index + 3, column + 1).Value = rows[index][column];

        sheet.SheetView.FreezeRows(2);
        sheet.Columns().AdjustToContents(2, 60);
        sheet.Column(Headers.Length - 1).Width = Math.Min(sheet.Column(Headers.Length - 1).Width, 40);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private void PrintClicked(object sender, RoutedEventArgs args)
    {
        if (Model is not { } model || model.Results.Count == 0) return;
        try
        {
            var dialog = new PrintDialog();
            dialog.PrintTicket.PageOrientation = PageOrientation.Landscape;
            if (dialog.ShowDialog() != true) return;
            var document = BuildDocument(model);
            dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, model.Localization["D_PostingManagement"]);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            model.Operation.Report(exception, OperationKind.Read);
        }
    }

    private static FlowDocument BuildDocument(PostingManagementViewModel model)
    {
        var document = new FlowDocument
        {
            FlowDirection = FlowDirection.RightToLeft,
            FontFamily = new FontFamily("Segoe UI, Tahoma"),
            FontSize = 10,
            PagePadding = new Thickness(30),
            ColumnWidth = double.PositiveInfinity,
        };
        document.Blocks.Add(new Paragraph(new Run(model.Localization["D_PostingManagement"]))
        {
            FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4),
        });
        document.Blocks.Add(new Paragraph(new Run(model.TotalsText)) { FontSize = 10, Margin = new Thickness(0, 0, 0, 10) });

        var table = new Table { CellSpacing = 0 };
        foreach (var _ in Headers) table.Columns.Add(new TableColumn());
        var groups = new TableRowGroup();
        table.RowGroups.Add(groups);

        var header = new TableRow { Background = Brushes.Gainsboro };
        foreach (var title in Headers)
            header.Cells.Add(new TableCell(new Paragraph(new Run(title)) { FontWeight = FontWeights.Bold, FontSize = 9 })
            {
                BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(2),
            });
        groups.Rows.Add(header);

        foreach (var row in model.Results)
        {
            var line = new TableRow();
            foreach (var value in Row(row))
                line.Cells.Add(new TableCell(new Paragraph(new Run(value)) { FontSize = 8.5 })
                {
                    BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(0, 0, 0, 0.5), Padding = new Thickness(2),
                });
            groups.Rows.Add(line);
        }

        document.Blocks.Add(table);
        return document;
    }
}
