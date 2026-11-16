using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using ClosedXML.Excel;
using PetraERP.Reporting.OpenSource;

namespace PetraERP.Reporting.Tests;

public sealed class ProfessionalReportExportTests(ITestOutputHelper output)
{
    private static readonly string[] ExpectedSheetNames = ["Transactions", "Summary", "Filters"];
    [Fact]
    public void SnapshotCopiesRowsFiltersColumnsAndAuthoritativeMeasures()
    {
        var row = new Dictionary<string, object?> { ["Narration"] = "ذمة الزبون", ["Amount"] = "12.3400" };
        var columns = new List<ReportColumn> { new("Narration", "البيان"), new("Amount", "المبلغ", ReportValueKind.Amount, 4) };
        var filters = new Dictionary<string, string> { ["بحث"] = "أحمد" };
        var measures = new List<ReportSummaryValue> { new("ActualBalance", "الرصيد الفعلي", 900m, "SYP") };
        var execution = Guid.NewGuid();
        var context = new ReportExecutionContext(Guid.NewGuid(), Guid.NewGuid());
        var snapshot = new ReportDocument("ledger", "دفتر الأستاذ", "شركة صناعية", "2026", "SYP", columns, [row],
            filters, executionId: execution, context: context, summaries: measures);
        row["Amount"] = 999m;
        filters["بحث"] = "تم التغيير";
        columns.Clear();
        measures.Clear();
        Assert.Equal(12.3400m, snapshot.Rows[0]["Amount"]);
        Assert.Equal("أحمد", snapshot.Filters["بحث"]);
        Assert.Equal(2, snapshot.Columns.Count);
        Assert.Equal(900m, Assert.Single(snapshot.Summaries).Value);
        Assert.Equal(execution, snapshot.ExecutionId);
        Assert.Equal(context, snapshot.Context);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, object?>)snapshot.Rows[0]).Add("Injected", 1m));
    }

    [Fact]
    public void SnapshotRejectsBinaryFloatingPointAndInvalidNumbersAndPreservesNull()
    {
        Assert.Throws<FormatException>(() => AmountSnapshot(0.1d));
        Assert.Throws<FormatException>(() => AmountSnapshot("1,234"));
        Assert.Throws<FormatException>(() => AmountSnapshot("0.00000000000000000000000000001"));
        Assert.Throws<FormatException>(() => AmountSnapshot("0.1234567890123456789012345678901"));
        Assert.Null(AmountSnapshot(null).Rows[0]["Amount"]);
        Assert.Throws<FormatException>(() => ReportDocument.DecimalValue(null));
        Assert.Throws<ArgumentException>(() => new ReportDocument("test", "اختبار", "شركة", "2026", "SYP",
            [new("Index", "الترتيب", ReportValueKind.Integer)], [new Dictionary<string, object?> { ["Index"] = 1.2m }]));
    }

    [Theory]
    [InlineData("=SUM(1,2)")]
    [InlineData(" \t\r\n=HYPERLINK(\"https://example.invalid\")")]
    [InlineData("\u200f\u0001+SUM(1,2)")]
    [InlineData("@SUM(1,2)")]
    [InlineData("-1+2")]
    public void CsvProtectsSpreadsheetFormulasAfterWhitespaceControlsAndBidi(string text)
    {
        var report = TextSnapshot(text);
        var csv = Encoding.UTF8.GetString(new CsvReportExporter().Export(report, includeBom: false));
        Assert.Equal("\"البيان\"\r\n\"'" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"\r\n", csv);
    }

    [Fact]
    public void CsvPreservesArabicQuotesNewlinesExactFinancialValuesAndOptionalBom()
    {
        var report = new ReportDocument("ledger", "دفتر", "الشركة", "2026", "SYP",
            [new("Narration", "البيان"), new("Amount", "المبلغ", ReportValueKind.Amount, 4)],
            [new Dictionary<string, object?> { ["Narration"] = "أحمد, \"فاتورة\"\r\nعربية", ["Amount"] = -1234567890123456.7890m }]);
        var exporter = new CsvReportExporter();
        var withBom = exporter.Export(report);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, withBom[..3]);
        var csv = Encoding.UTF8.GetString(exporter.Export(report, false));
        Assert.Equal("\"البيان\",\"المبلغ\"\r\n\"أحمد, \"\"فاتورة\"\"\r\nعربية\",\"-1234567890123456.7890\"\r\n", csv);
    }

    [Theory]
    [InlineData(ExcelExportLayout.Analytical, 1)]
    [InlineData(ExcelExportLayout.PrintLayout, 7)]
    public void ExcelUsesThreeSheetsNumericDatesAndAuthoritativeSummaryWithoutFormulas(ExcelExportLayout layout, int headerRow)
    {
        var report = new ReportDocument("ledger", "دفتر الأستاذ [untrusted]", "شركة الاختبار", "2026", "SYP",
            [new("Narration", "البيان"), new("Amount", "المبلغ", ReportValueKind.Amount, 4), new("Date", "التاريخ", ReportValueKind.Date)],
            [new Dictionary<string, object?> { ["Narration"] = " \t=SUM(A1:A2)", ["Amount"] = 12.3400m, ["Date"] = new DateOnly(2026, 10, 22) }],
            new Dictionary<string, string> { ["بحث"] = "=HYPERLINK(\"unsafe\")" },
            summaries: [new("ActualBalance", "الرصيد المحاسبي الفعلي", 900.1234m, "SYP")]);
        using var stream = new MemoryStream(new ExcelReportExporter().Export(report, layout));
        using var workbook = new XLWorkbook(stream);
        Assert.Equal(ExpectedSheetNames, workbook.Worksheets.Select(x => x.Name));
        var transactions = workbook.Worksheet("Transactions");
        Assert.True(transactions.RightToLeft);
        Assert.True(transactions.AutoFilter.IsEnabled);
        Assert.Equal(" \t=SUM(A1:A2)", transactions.Cell(headerRow + 1, 1).GetString());
        Assert.Equal(XLDataType.Number, transactions.Cell(headerRow + 1, 2).DataType);
        Assert.Equal(12.3400m, transactions.Cell(headerRow + 1, 2).GetValue<decimal>());
        Assert.Equal(new DateTime(2026, 10, 22), transactions.Cell(headerRow + 1, 3).GetDateTime());
        Assert.Equal(900.1234m, workbook.Worksheet("Summary").Cell(7, 2).GetValue<decimal>());
        Assert.All(workbook.Worksheets.SelectMany(x => x.CellsUsed()), cell => Assert.False(cell.HasFormula));
    }

    [Theory]
    [InlineData("0.1", true)]
    [InlineData("-5000.1250", true)]
    [InlineData("123456789012345", true)]
    [InlineData("0.0000000000000000000000000001", true)]
    [InlineData("1234567890123456.7890", false)]
    [InlineData("79228162514264337593543950335", false)]
    public void ExcelNeverSilentlyRoundsAmountsBeyondItsPrecision(string amount, bool numeric)
    {
        var result = new ExcelReportExporter().ExportDetailed(AmountSnapshot(amount));
        using var stream = new MemoryStream(result.Content);
        using var workbook = new XLWorkbook(stream);
        var cell = workbook.Worksheet("Transactions").Cell(2, 1);
        Assert.Equal(numeric ? XLDataType.Number : XLDataType.Text, cell.DataType);
        if (numeric)
        {
            Assert.Equal(decimal.Parse(amount, CultureInfo.InvariantCulture), cell.GetValue<decimal>());
            Assert.Empty(result.PrecisionWarnings);
        }
        else
        {
            Assert.Equal(decimal.Parse(amount, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture), cell.GetString());
            Assert.Equal(new ExcelPrecisionWarning("Amount", 1), Assert.Single(result.PrecisionWarnings));
            Assert.Contains(workbook.Worksheet("Filters").CellsUsed(), x => x.GetString() == "LOSSLESS_TEXT: Amount");
        }
    }

    [Theory]
    [InlineData(10_000)]
    [InlineData(100_000)]
    public void LargeSnapshotExportsEveryRowThroughCsvAndExcel(int rowCount)
    {
        var timer = Stopwatch.StartNew();
        var memory = GC.GetTotalMemory(false);
        var report = new ReportDocument("synthetic", "اختبار الحجم", "بيانات صناعية", "2026", "SYP",
            [new("Index", "الترتيب", ReportValueKind.Integer), new("Narration", "البيان")],
            Enumerable.Range(1, rowCount).Select(index => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
            { ["Index"] = index, ["Narration"] = "بيان " + index.ToString(CultureInfo.InvariantCulture) }));
        var csv = new CsvReportExporter().Export(report, false);
        var content = new ExcelReportExporter().Export(report);
        using var stream = new MemoryStream(content);
        using var workbook = new XLWorkbook(stream);
        Assert.Equal(rowCount + 1, workbook.Worksheet("Transactions").LastRowUsed()!.RowNumber());
        Assert.Equal(rowCount, workbook.Worksheet("Transactions").Cell(rowCount + 1, 1).GetValue<int>());
        Assert.EndsWith("\"" + rowCount.ToString(CultureInfo.InvariantCulture) + "\",\"بيان " + rowCount.ToString(CultureInfo.InvariantCulture) + "\"\r\n",
            Encoding.UTF8.GetString(csv), StringComparison.Ordinal);
        output.WriteLine("Synthetic rows={0}; elapsed={1}; managed delta={2}; CSV bytes={3}; XLSX bytes={4}",
            rowCount, timer.Elapsed, GC.GetTotalMemory(false) - memory, csv.Length, content.Length);
    }

    [Fact]
    public void SnapshotRejectsOverLimitRatherThanSilentlyTruncating()
    {
        Assert.Throws<ArgumentException>(() => new ReportDocument("synthetic", "تقرير", "شركة", "2026", "SYP",
            [new("Index", "الترتيب", ReportValueKind.Integer)], Enumerable.Range(1, ReportDocument.MaximumRows + 1)
                .Select(index => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["Index"] = index })));
    }

    [Fact]
    public void HtmlEncodesUntrustedContentAndKeepsAuthoritativeMeasures()
    {
        var html = new HtmlPreviewExporter().Export(new ReportDocument("ledger", "<script>عنوان</script>", "شركة", "2026", "SYP",
            [new("Narration", "<img>")], [new Dictionary<string, object?> { ["Narration"] = "<script>alert('x')</script>" }],
            summaries: [new("ActualBalance", "الرصيد", 900m, "SYP")]));
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("900", html, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsPdfAndRasterPagesRenderSyntheticArabicWithoutExecutingHeaderExpressions()
    {
        var report = new ReportDocument("arabic-proof", "دفتر الأستاذ [UntrustedExpression()]", "شركة البتراء للاختبار الصناعي", "2026-10-01 — 2026-10-22", "SYP",
            [new("Narration", "البيان [Unsafe()]"), new("Debit", "مدين", ReportValueKind.Amount, 4), new("Credit", "دائن", ReportValueKind.Amount, 4)],
            Enumerable.Range(1, 100).Select(index => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
            { ["Narration"] = "فاتورة أحمد — البضاعة، مستودع حلب [DoNotEvaluate()]", ["Debit"] = 100.1234m, ["Credit"] = 0m }),
            summaries: [new("ActualClosing", "الرصيد الفعلي", 50000.1234m, "SYP"), new("FilteredDebit", "مدين الحركات المصفاة", 10012.3400m, "SYP")]);
        var exporter = new FastReportPdfExporter();
        var pdf = exporter.Export(report);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5));
        var images = exporter.ExportPageImages(report, 5);
        Assert.True(images.Count >= 2);
        Assert.All(images, image => Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, image[..8]));
        var artifactDirectory = Environment.GetEnvironmentVariable("PETRA_REPORTING_ARTIFACT_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(artifactDirectory))
        {
            Directory.CreateDirectory(artifactDirectory);
            File.WriteAllBytes(Path.Combine(artifactDirectory, "synthetic-arabic.pdf"), pdf);
            for (var index = 0; index < images.Count; index++)
                File.WriteAllBytes(Path.Combine(artifactDirectory, $"synthetic-arabic-page-{index + 1}.png"), images[index]);
            File.WriteAllBytes(Path.Combine(artifactDirectory, "synthetic-arabic.xlsx"), new ExcelReportExporter().Export(report));
            output.WriteLine("Visual Arabic/print acceptance still required: " + artifactDirectory);
        }
    }

    private static ReportDocument AmountSnapshot(object? amount) => new("test", "تقرير", "شركة", "2026", "SYP",
        [new("Amount", "المبلغ", ReportValueKind.Amount, 4)], [new Dictionary<string, object?> { ["Amount"] = amount }]);

    private static ReportDocument TextSnapshot(string text) => new("test", "تقرير", "شركة", "2026", "SYP",
        [new("Narration", "البيان")], [new Dictionary<string, object?> { ["Narration"] = text }]);
}

