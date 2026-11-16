using System.Diagnostics;
using System.IO;
using PetraERP.ApiClient;
using PetraERP.Desktop.Accounting;
using PetraERP.Desktop.Sales;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

/// <summary>
/// Measures the actual execution times (ms) for the 6 core benchmark operations:
/// 1. Chart of Accounts (دليل الحسابات)
/// 2. Item Directory (دليل المواد)
/// 3. Invoice List (قائمة الفواتير)
/// 4. Invoice Entry (إدخال فاتورة)
/// 5. Save & Post (حفظ وترحيل)
/// 6. Ledger (دفتر الأستاذ)
/// </summary>
public sealed class PetraPerformanceBenchmarkTests
{
    private static async Task<(AccountingFixture Accounting, DesktopViewModel Shell)> SetupEnvironmentAsync(int accountCount = 1000, int itemCount = 1000, int invoiceCount = 1000)
    {
        var fixture = new ShellFixture();
        fixture.User.Permissions = [
            "Ledger.Accounts.View", "Ledger.Accounts.Manage", "Ledger.Journals.View", "Ledger.Journals.Post", "Ledger.Reports.View",
            "Inventory.Items.View", "Inventory.Movements.View",
            "Sales.Invoices.View", "Sales.Invoices.Manage", "Sales.Invoices.Approve", "Sales.Invoices.Post"
        ];

        var accounting = new AccountingFixture();

        // 1. Seed Accounts (1,000 accounts)
        var rootId = Guid.NewGuid();
        accounting.Accounts.Add(new AccountResponse { Id = rootId, Code = "1", NameArabic = "الأصول", PostingAllowed = false, IsActive = true });
        for (int i = 1; i <= accountCount; i++)
        {
            accounting.Accounts.Add(new AccountResponse
            {
                Id = Guid.NewGuid(),
                Code = $"1{i:D5}",
                NameArabic = i % 2 == 0 ? $"حساب زبون تجريبي {i}" : $"مصرف تجاري {i}",
                NameEnglish = $"Account {i}",
                ParentId = rootId,
                PostingAllowed = true,
                IsActive = true
            });
        }

        // 2. Seed Item Groups & Items (1,000 items across 20 groups)
        var groupIds = new List<Guid>();
        for (int g = 1; g <= 20; g++)
        {
            var gid = Guid.NewGuid();
            groupIds.Add(gid);
            accounting.ItemGroups.Add(new ItemGroupDto
            {
                Id = gid,
                Code = $"GRP-{g:D2}",
                NameArabic = $"مجموعة المواد {g}",
                IsActive = true
            });
        }
        for (int i = 1; i <= itemCount; i++)
        {
            accounting.Items.Add(new ItemDetailDto
            {
                Id = Guid.NewGuid(),
                Code = $"ITM-{i:D5}",
                NameArabic = i % 3 == 0 ? $"هاتف ذكي طراز {i}" : $"شاشة حاسوب {i}",
                GroupId = groupIds[i % groupIds.Count],
                IsActive = true
            });
        }

        // 3. Seed Invoices (1,000 invoices)
        for (int i = 1; i <= invoiceCount; i++)
        {
            accounting.Invoices.Add(new SalesInvoiceSummaryDto
            {
                Id = Guid.NewGuid(),
                InvoiceNumber = $"INV-{i:D6}",
                Status = i % 4 == 0 ? SalesInvoiceStatus.Posted : SalesInvoiceStatus.Draft,
                GrandTotal = (i * 15.5m).ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
                CustomerName = $"عميل {i}",
                Version = "AAAAAAAAB9I="
            });
        }

        var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin";
        shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        return (accounting, shell);
    }

    [Fact]
    public async Task BenchmarkAllSixOperationsRecordBaseline()
    {
        var (accounting, shell) = await SetupEnvironmentAsync().ConfigureAwait(true);
        using var _ = shell;

        var sw = new Stopwatch();

        // ---------------------------------------------------------
        // 1. Chart of Accounts (دليل الحسابات)
        // ---------------------------------------------------------
        sw.Restart();
        await shell.NavigateToAsync("account-directory", null).ConfigureAwait(true);
        var accountDoc = shell.Documents.Documents.Single(d => d.Route == "account-directory");
        var accountPage = Assert.IsType<AccountDirectoryViewModel>(accountDoc.Content);
        // Keystroke search across 1000 accounts
        accountPage.SearchText = "مصرف";
        await Task.Delay(100, TestContext.Current.CancellationToken).ConfigureAwait(true);
        sw.Stop();
        var chartOfAccountsMs = sw.ElapsedMilliseconds;

        // ---------------------------------------------------------
        // 2. Item Directory (دليل المواد)
        // ---------------------------------------------------------
        sw.Restart();
        await shell.NavigateToAsync("item-directory", null).ConfigureAwait(true);
        var itemDoc = shell.Documents.Documents.Single(d => d.Route == "item-directory");
        var itemPage = Assert.IsType<ItemDirectoryViewModel>(itemDoc.Content);
        // Keystroke search across 1000 items
        itemPage.SearchText = "هاتف";
        await Task.Delay(100, TestContext.Current.CancellationToken).ConfigureAwait(true);
        sw.Stop();
        var itemDirectoryMs = sw.ElapsedMilliseconds;

        // ---------------------------------------------------------
        // 3. Invoice List (قائمة الفواتير)
        // ---------------------------------------------------------
        sw.Restart();
        await shell.NavigateToAsync("sales-invoices", null).ConfigureAwait(true);
        var invoiceDoc = shell.Documents.Documents.Single(d => d.Route == "sales-invoices");
        var invoiceListPage = Assert.IsType<SalesInvoicesViewModel>(invoiceDoc.Content);
        // Apply filter/search
        invoiceListPage.Search = "INV-0005";
        await Task.Delay(100, TestContext.Current.CancellationToken).ConfigureAwait(true);
        sw.Stop();
        var invoiceListMs = sw.ElapsedMilliseconds;

        // ---------------------------------------------------------
        // 4. Invoice Entry Initialization (إدخال فاتورة)
        // ---------------------------------------------------------
        sw.Restart();
        await shell.NavigateToAsync("sales-invoice-card", new NewSalesInvoiceCardRequest()).ConfigureAwait(true);
        var invoiceEntryPage = Assert.IsType<SalesInvoiceCardViewModel>(shell.Documents.Active!.Content);
        sw.Stop();
        var invoiceEntryMs = sw.ElapsedMilliseconds;

        // ---------------------------------------------------------
        // 5. Save & Post (حفظ وترحيل)
        // ---------------------------------------------------------
        sw.Restart();
        var saveInvoiceId = Guid.NewGuid();
        accounting.SalesDetail = new SalesInvoiceDetailDto
        {
            Id = saveInvoiceId,
            Status = SalesInvoiceStatus.Draft,
            Version = "AAAAAAAAB9E=",
            SubTotal = "1000.0000",
            DiscountTotal = "50.0000",
            TaxTotal = "95.0000",
            AdditionsTotal = "0.0000",
            GrandTotal = "1045.0000",
            ExchangeRate = "1.00000000",
            Lines = []
        };
        await shell.NavigateToAsync("sales-invoice-card", new OpenSalesInvoiceCardRequest(saveInvoiceId)).ConfigureAwait(true);
        var postCard = Assert.IsType<SalesInvoiceCardViewModel>(shell.Documents.Active!.Content);
        await postCard.ApproveCommand.ExecuteAsync(null).ConfigureAwait(true);
        await postCard.PostCommand.ExecuteAsync(null).ConfigureAwait(true);
        sw.Stop();
        var saveAndPostMs = sw.ElapsedMilliseconds;

        // ---------------------------------------------------------
        // 6. Ledger (دفتر الأستاذ)
        // ---------------------------------------------------------
        sw.Restart();
        var ledgerAccountId = Guid.NewGuid();
        accounting.Accounts.Add(new AccountResponse { Id = ledgerAccountId, Code = "1101", NameArabic = "الصندوق الرئيسي", PostingAllowed = true, IsActive = true });
        accounting.Ledger = new AccountLedgerResponse
        {
            Account = new() { Id = ledgerAccountId, Code = "1101", NameArabic = "الصندوق الرئيسي" },
            Lines = Enumerable.Range(1, 1000).Select(i => new AccountLedgerLineResponse
            {
                JournalId = Guid.NewGuid(),
                LineId = Guid.NewGuid(),
                BranchId = Guid.NewGuid(),
                AccountingDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-i)),
                PostingSequence = i,
                TransactionDebit = i % 2 == 0 ? "500.0000" : "0.0000",
                TransactionCredit = i % 2 == 1 ? "300.0000" : "0.0000",
                BaseDebit = i % 2 == 0 ? "500.0000" : "0.0000",
                BaseCredit = i % 2 == 1 ? "300.0000" : "0.0000",
                JournalDescription = $"قيد محاسبي رقم {i}",
                LineDescription = $"سطر {i}",
                AccountCode = "1101",
                TransactionCurrencyCode = "USD",
                RunningBalance = (i * 200m).ToString("F4", System.Globalization.CultureInfo.InvariantCulture)
            }).ToArray(),
            Page = 1,
            PageSize = 1000
        };
        await shell.NavigateToAsync("account-ledger", new OpenAccountLedgerRequest(ledgerAccountId, null, null, false)).ConfigureAwait(true);
        var ledgerDoc = shell.Documents.Documents.Single(d => d.Route == "account-ledger");
        var ledgerPage = Assert.IsType<AccountLedgerViewModel>(ledgerDoc.Content);
        await ledgerPage.RunCommand.ExecuteAsync(null).ConfigureAwait(true);
        sw.Stop();
        var ledgerMs = sw.ElapsedMilliseconds;

        // Output measured benchmark timings to test runner
        TestContext.Current.SendDiagnosticMessage($"[BENCHMARK_RESULT] ChartOfAccounts: {chartOfAccountsMs} ms");
        TestContext.Current.SendDiagnosticMessage($"[BENCHMARK_RESULT] ItemDirectory: {itemDirectoryMs} ms");
        TestContext.Current.SendDiagnosticMessage($"[BENCHMARK_RESULT] InvoiceList: {invoiceListMs} ms");
        TestContext.Current.SendDiagnosticMessage($"[BENCHMARK_RESULT] InvoiceEntry: {invoiceEntryMs} ms");
        TestContext.Current.SendDiagnosticMessage($"[BENCHMARK_RESULT] SaveAndPost: {saveAndPostMs} ms");
        TestContext.Current.SendDiagnosticMessage($"[BENCHMARK_RESULT] Ledger: {ledgerMs} ms");

        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "baseline_benchmark_results.txt"),
            $"ChartOfAccounts={chartOfAccountsMs}\nItemDirectory={itemDirectoryMs}\nInvoiceList={invoiceListMs}\nInvoiceEntry={invoiceEntryMs}\nSaveAndPost={saveAndPostMs}\nLedger={ledgerMs}\n");

        Assert.True(chartOfAccountsMs > 0);
        Assert.True(itemDirectoryMs > 0);
        Assert.True(invoiceListMs > 0);
        Assert.True(invoiceEntryMs > 0);
        Assert.True(saveAndPostMs > 0);
        Assert.True(ledgerMs > 0);
    }
}
