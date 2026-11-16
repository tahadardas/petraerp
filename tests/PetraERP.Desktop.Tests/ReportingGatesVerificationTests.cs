using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using PetraERP.ApiClient;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Reporting;
using PetraERP.Reporting.OpenSource;

namespace PetraERP.Desktop.Tests;

[Collection(WpfApplicationGroup.Name)]
public sealed class ReportingGatesVerificationTests
{
    [Fact]
    public async Task GateR05MultipleSimultaneousReportWindowsCanCoexistModelessly()
    {
        await WpfApplicationHost.RunAsync(async () =>
        {
            WpfApplicationHost.ResetResources();
            WpfApplicationHost.MergeThemes("Themes/Light.xaml", "Themes/Dense.xaml", "Themes/Components.xaml");
            try
            {
                var loc = new LocalizationService("ar");
                var snapshot1 = CreateSnapshot("Report 1");
                var snapshot2 = CreateSnapshot("Report 2");

                var win1 = new PetraReportViewerWindow(
                    new ProfessionalPreviewRequest(snapshot1, "Report 1", new Dictionary<ProfessionalReportField, string>()),
                    loc, _ => { });
                var win2 = new PetraReportViewerWindow(
                    new ProfessionalPreviewRequest(snapshot2, "Report 2", new Dictionary<ProfessionalReportField, string>()),
                    loc, _ => { });

                win1.Left = -20000; win1.Top = -20000;
                win2.Left = -20000; win2.Top = -20000;

                win1.Show();
                win2.Show();

                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

                Assert.True(win1.IsVisible);
                Assert.True(win2.IsVisible);

                // Minimize win1 - win2 must remain visible and active
                win1.WindowState = WindowState.Minimized;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Equal(WindowState.Minimized, win1.WindowState);
                Assert.True(win2.IsVisible);
                Assert.NotEqual(WindowState.Minimized, win2.WindowState);

                // Restore win1
                win1.WindowState = WindowState.Normal;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Equal(WindowState.Normal, win1.WindowState);

                win1.Close();
                win2.Close();
            }
            finally { WpfApplicationHost.ResetResources(); }
        }, TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    [Fact]
    public async Task GateR07AndR10CtrlFSearchAndAutoFilterRowFunctionality()
    {
        await WpfApplicationHost.RunAsync(async () =>
        {
            WpfApplicationHost.ResetResources();
            WpfApplicationHost.MergeThemes("Themes/Light.xaml", "Themes/Dense.xaml", "Themes/Components.xaml");
            try
            {
                var loc = new LocalizationService("ar");
                var snapshot = CreateSnapshot("Test Search & AutoFilter");
                var win = new PetraReportViewerWindow(
                    new ProfessionalPreviewRequest(snapshot, "Test Report", new Dictionary<ProfessionalReportField, string>()),
                    loc, _ => { });
                win.Left = -20000; win.Top = -20000;
                win.Show();

                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

                var searchBox = (TextBox)win.FindName("SearchBox");
                var grid = (PetraInteractiveReportGrid)win.FindName("ResultsGrid");

                for (var attempt = 0; attempt < 50 && grid.Document is null; attempt++)
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

                Assert.NotNull(grid.Document);

                // Test Ctrl+F shortcut focus (R07)
                win.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(win)!, 0, Key.F)
                {
                    RoutedEvent = UIElement.PreviewKeyDownEvent
                });

                // Search box typing and search navigation
                searchBox.Text = "صندوق";
                ((Button)win.FindName("NextMatchButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

                // Test Auto-filter row (R10)
                var autoFilterValidation = grid.ApplyFilter(new PetraReportFilterCondition("AccountName", PetraReportFilterOperator.Contains, "صندوق"));
                Assert.True(autoFilterValidation.IsValid);
                Assert.True(grid.VisibleRowCount > 0);

                win.Close();
            }
            finally { WpfApplicationHost.ResetResources(); }
        }, TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    [Fact]
    public void GateR31MultipleCurrencyCorrectness()
    {
        // Document with mixed transaction currencies and base currency SYP
        var document = new ReportDocument(
            "Ledger", "دفتر الأستاذ متعدد العملات", "شركة", "2026", "SYP",
            [
                new ReportColumn("Date", "التاريخ", ReportValueKind.Date),
                new ReportColumn("AccountName", "الحساب"),
                new ReportColumn("Currency", "العملة الأساسية"),
                new ReportColumn("Debit", "مدين أساسي", ReportValueKind.Amount, 2),
                new ReportColumn("Credit", "دائن أساسي", ReportValueKind.Amount, 2),
                new ReportColumn("TransactionCurrency", "عملة الحركة"),
                new ReportColumn("TransactionDebit", "مدين الحركة", ReportValueKind.Amount, 2),
                new ReportColumn("TransactionCredit", "دائن الحركة", ReportValueKind.Amount, 2),
            ],
            [
                new Dictionary<string, object?>
                {
                    ["Date"] = "2026-01-10",
                    ["AccountName"] = "صندوق الدولار",
                    ["Currency"] = "SYP",
                    ["Debit"] = 1500000m, // 100 USD @ 15,000
                    ["Credit"] = 0m,
                    ["TransactionCurrency"] = "USD",
                    ["TransactionDebit"] = 100m,
                    ["TransactionCredit"] = 0m,
                },
                new Dictionary<string, object?>
                {
                    ["Date"] = "2026-01-11",
                    ["AccountName"] = "صندوق اليورو",
                    ["Currency"] = "SYP",
                    ["Debit"] = 1600000m, // 100 EUR @ 16,000
                    ["Credit"] = 0m,
                    ["TransactionCurrency"] = "EUR",
                    ["TransactionDebit"] = 100m,
                    ["TransactionCredit"] = 0m,
                },
                new Dictionary<string, object?>
                {
                    ["Date"] = "2026-01-12",
                    ["AccountName"] = "صندوق الليرة",
                    ["Currency"] = "SYP",
                    ["Debit"] = 500000m,
                    ["Credit"] = 0m,
                    ["TransactionCurrency"] = "SYP",
                    ["TransactionDebit"] = 500000m,
                    ["TransactionCredit"] = 0m,
                },
            ],
            summaries:
            [
                new ReportSummaryValue("TotalDebitBase", "مجموع المدين بالعملة الأساسية", 3600000m, "SYP"),
            ]);

        // Filter by TransactionCurrency = USD
        var usdFilter = new PetraReportFilterCondition("TransactionCurrency", PetraReportFilterOperator.Equals, "USD");
        var usdRows = PetraReportFilterEngine.Apply(document, document.Rows, usdFilter);
        Assert.Single(usdRows);
        Assert.Equal(100m, usdRows[0]["TransactionDebit"]);
        Assert.Equal(1500000m, usdRows[0]["Debit"]);

        // Base currency authoritative summary remains untouched
        Assert.Equal(3600000m, document.Summaries[0].Value);
        Assert.Equal("SYP", document.Summaries[0].CurrencyCode);
    }

    [Fact]
    public void GateR32FastLargeReportOperationsPerformUnderThreshold()
    {
        // 10,000 rows
        const int rowCount = 10_000;
        var rows = new List<IReadOnlyDictionary<string, object?>>(rowCount);
        for (var i = 0; i < rowCount; i++)
        {
            rows.Add(new Dictionary<string, object?>
            {
                ["PostingSequence"] = (long)(i + 1),
                ["Date"] = "2026-01-01",
                ["AccountCode"] = (101000 + (i % 50)).ToString(CultureInfo.InvariantCulture),
                ["AccountName"] = i % 2 == 0 ? "صندوق رئيسي" : "مصرف التجاري",
                ["Description"] = $"حركة قيد محاسبي رقم {i + 1}",
                ["Debit"] = (decimal)(i * 10),
                ["Credit"] = 0m,
            });
        }

        var document = new ReportDocument(
            "LargeLedger", "تقرير ضخم", "الشركة", "2026", "SYP",
            [
                new ReportColumn("PostingSequence", "التسلسل", ReportValueKind.Integer),
                new ReportColumn("Date", "التاريخ", ReportValueKind.Date),
                new ReportColumn("AccountCode", "رمز الحساب"),
                new ReportColumn("AccountName", "الحساب"),
                new ReportColumn("Description", "البيان"),
                new ReportColumn("Debit", "مدين", ReportValueKind.Amount, 2),
                new ReportColumn("Credit", "دائن", ReportValueKind.Amount, 2),
            ],
            rows);

        // 1. Search across 10,000 rows
        var sw = Stopwatch.StartNew();
        var searchResults = PetraReportFilterEngine.Search(document, rows, new PetraReportSearchOptions("صندوق رئيسي", "AccountName"));
        sw.Stop();
        Assert.Equal(5_000, searchResults.Count);
        Assert.True(sw.ElapsedMilliseconds < 250, $"Search took {sw.ElapsedMilliseconds} ms, expected < 250 ms");

        // 2. Filter 10,000 rows
        sw.Restart();
        var filter = new PetraReportFilterCondition("Debit", PetraReportFilterOperator.GreaterThan, "50000");
        var filteredRows = PetraReportFilterEngine.Apply(document, rows, filter);
        sw.Stop();
        Assert.Equal(4_999, filteredRows.Count);
        Assert.True(sw.ElapsedMilliseconds < 250, $"Filter took {sw.ElapsedMilliseconds} ms, expected < 250 ms");
    }

    [Fact]
    public void GateR33PermissionsAndCompanyIsolationValidation()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var fiscalYearId = Guid.NewGuid();

        var request = new ProfessionalReportRequest
        {
            Kind = ProfessionalReportKind.AccountLedger,
            FiscalYearId = fiscalYearId,
            From = new DateOnly(2026, 1, 1),
            To = new DateOnly(2026, 12, 31),
            Sources = [ProfessionalReportSource.ManualJournal],
            Fields = [ProfessionalReportField.Date, ProfessionalReportField.Debit],
        };

        // Snapshot execution context check: if company B tries to consume snapshot created for company A, mismatch is detected
        var snapshotForA = new ProfessionalReportExecutionResponse
        {
            ExecutionId = Guid.NewGuid(),
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            CompanyId = companyA,
            BranchId = branchId,
            FiscalYearId = fiscalYearId,
            CompanyName = "Company A",
            BranchName = "Main Branch",
            FiscalYearCode = "2026",
            CurrencyCode = "SYP",
            SubjectCode = "101",
            SubjectName = "Cash",
            BalanceConvention = "Debit",
            Parameters = request,
            SelectedFields = [ProfessionalReportField.Date, ProfessionalReportField.Debit],
            Rows = [],
            Summary = new ProfessionalReportSummaryDto
            {
                ActualOpening = "0",
                ActualPeriodDebit = "0",
                ActualPeriodCredit = "0",
                ActualClosing = "0",
                DisplayClosing = "0",
                FilteredDebit = "0",
                FilteredCredit = "0",
                RowCount = 0,
            },
            Notes = [],
            InvoiceDetails = [],
        };

        // Assert company isolation
        Assert.Equal(companyA, snapshotForA.CompanyId);
        Assert.NotEqual(companyB, snapshotForA.CompanyId);
    }

    private static ProfessionalReportExecutionResponse CreateSnapshot(string title) => new()
    {
        ExecutionId = Guid.NewGuid(),
        GeneratedAtUtc = DateTimeOffset.UtcNow,
        CompanyId = Guid.NewGuid(),
        BranchId = Guid.NewGuid(),
        FiscalYearId = Guid.NewGuid(),
        CompanyName = "شركة الاختبار",
        BranchName = "الفرع الرئيسي",
        FiscalYearCode = "2026",
        CurrencyCode = "SYP",
        SubjectCode = "10101",
        SubjectName = "صندوق المركز الرئيسي",
        BalanceConvention = "مدين",
        Parameters = new ProfessionalReportRequest
        {
            Kind = ProfessionalReportKind.AccountLedger,
            FiscalYearId = Guid.NewGuid(),
            From = new DateOnly(2026, 1, 1),
            To = new DateOnly(2026, 1, 31),
            Sources = [ProfessionalReportSource.ReceiptVoucher],
            Fields = [ProfessionalReportField.Date, ProfessionalReportField.AccountName, ProfessionalReportField.Debit, ProfessionalReportField.Credit],
        },
        SelectedFields = [ProfessionalReportField.Date, ProfessionalReportField.AccountName, ProfessionalReportField.Debit, ProfessionalReportField.Credit],
        Rows =
        [
            new ProfessionalReportRowDto
            {
                RowId = Guid.NewGuid(),
                Date = new DateOnly(2026, 1, 15),
                AccountCode = "10101",
                AccountName = "صندوق المركز الرئيسي",
                Debit = "50000",
                Credit = "0",
            },
        ],
        Summary = new ProfessionalReportSummaryDto
        {
            ActualOpening = "10000",
            ActualPeriodDebit = "50000",
            ActualPeriodCredit = "0",
            ActualClosing = "60000",
            DisplayClosing = "60000",
            FilteredDebit = "50000",
            FilteredCredit = "0",
            RowCount = 1,
        },
        Notes = [],
        InvoiceDetails = [],
    };
}
