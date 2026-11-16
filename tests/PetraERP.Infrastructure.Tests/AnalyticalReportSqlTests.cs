using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Application.GeneralLedger.Reports;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Infrastructure.GeneralLedger;

namespace PetraERP.Infrastructure.Tests;

public sealed class AnalyticalReportSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static async Task<(LedgerScenario Scenario, AccountDto Cash, AccountDto Sales, AccountDto Expense)> SeedAsync(SqlTestDatabase database)
    {
        var s = await LedgerScenario.CreateAsync(database).ConfigureAwait(false);
        var cash = await s.AccountAsync(s.CompanyA, "1101", AccountTypeCode.Assets, true).ConfigureAwait(false);
        var sales = await s.AccountAsync(s.CompanyA, "4101", AccountTypeCode.Revenue, true).ConfigureAwait(false);
        var expense = await s.AccountAsync(s.CompanyA, "5101", AccountTypeCode.Expenses, true).ConfigureAwait(false);
        await s.JournalAsync(s.CompanyA, s.BranchA1, new DateOnly(2026, 1, 5), "USD", true, (cash.Id, 1000m, 0m), (sales.Id, 0m, 1000m)).ConfigureAwait(false);
        await s.JournalAsync(s.CompanyA, s.BranchA1, new DateOnly(2026, 2, 10), "USD", true, (expense.Id, 200m, 0m), (cash.Id, 0m, 200m)).ConfigureAwait(false);
        await s.JournalAsync(s.CompanyA, s.BranchA1, new DateOnly(2026, 3, 1), "USD", false, (cash.Id, 9999m, 0m), (sales.Id, 0m, 9999m)).ConfigureAwait(false);
        await s.JournalAsync(s.CompanyA, s.BranchA2, new DateOnly(2026, 1, 20), "USD", true, (cash.Id, 500m, 0m), (sales.Id, 0m, 500m)).ConfigureAwait(false);
        return (s, cash, sales, expense);
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task MonthlyRevenueExpenseUsesPostedLinesOfTheBranchOnlyUnlessConsolidated()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var (s, _, _, _) = await SeedAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var service = new SqlLedgerQueryService(context);
            Assert.Contains("FIN-08", service.SupportedReportIds);
            var query = new AnalyticalQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), null, null);

            var branch = await service.RunAsync(LedgerReportScope.Create(s.CompanyA, s.BranchA1, s.Actor, false), "FIN-08", query, Cancellation).ConfigureAwait(true);
            Assert.Equal(2, branch.Rows.Count); // January (revenue) and February (expense); the March draft never appears
            Assert.Equal(["2026-01", "1000.0000", "0.0000", "1000.0000"], branch.Rows[0]);
            Assert.Equal(["2026-02", "0.0000", "200.0000", "-200.0000"], branch.Rows[1]);
            Assert.Equal(["المجموع", "1000.0000", "200.0000", "800.0000"], branch.Totals);

            var all = await service.RunAsync(LedgerReportScope.Create(s.CompanyA, s.BranchA1, s.Actor, true), "FIN-08", query, Cancellation).ConfigureAwait(true);
            Assert.Equal("1500.0000", all.Rows[0][1]); // the second branch's January sale joins only in consolidated scope
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task CashFlowFailsClosedWithoutMappedCashAccountsAndUnknownReportIsRejected()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var (s, _, _, _) = await SeedAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var service = new SqlLedgerQueryService(context);
            var scope = LedgerReportScope.Create(s.CompanyA, s.BranchA1, s.Actor, false);
            var query = new AnalyticalQuery(null, null, null, null);
            var missing = await Assert.ThrowsAsync<BusinessRuleException>(() => service.RunAsync(scope, "FIN-03", query, Cancellation)).ConfigureAwait(true);
            Assert.Equal("report.no_cash_accounts", missing.Code);
            await Assert.ThrowsAnyAsync<Exception>(() => service.RunAsync(scope, "NOPE-01", query, Cancellation)).ConfigureAwait(true);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task BranchResultsAndUnpostedEntriesReconcileWithTheSeededJournals()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var (s, _, _, _) = await SeedAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var service = new SqlLedgerQueryService(context);
            var all = LedgerReportScope.Create(s.CompanyA, s.BranchA1, s.Actor, true);
            var query = new AnalyticalQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), null, null);

            var results = await service.RunAsync(all, "FIN-09", query, Cancellation).ConfigureAwait(true);
            Assert.Equal(2, results.Rows.Count);
            Assert.Equal(["المجموع", "1500.0000", "200.0000", "1300.0000"], results.Totals);

            var unposted = await service.RunAsync(all, "GL-12", query, Cancellation).ConfigureAwait(true);
            var draft = Assert.Single(unposted.Rows);
            Assert.Equal("2026-03-01", draft[0]);
            Assert.Equal("9999.0000", draft[3]);
            Assert.Equal(draft[3], draft[4]);

            var negative = await service.RunAsync(all, "INV-11", query, Cancellation).ConfigureAwait(true);
            Assert.Empty(negative.Rows);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ThirdBatchReportsRunOnAnEmptyInventoryAndFxFailsClosedWithoutRoleAccounts()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var (s, _, _, _) = await SeedAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var service = new SqlLedgerQueryService(context);
            var scope = LedgerReportScope.Create(s.CompanyA, s.BranchA1, s.Actor, true);
            var query = new AnalyticalQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), null, null);
            foreach (var id in new[] { "INV-07", "INV-09", "INV-12", "GL-10", "GL-13", "FX-06" })
            {
                Assert.Contains(id, service.SupportedReportIds);
                var report = await service.RunAsync(scope, id, query, Cancellation).ConfigureAwait(true);
                Assert.Equal(id, report.ReportId);
                Assert.NotEmpty(report.Columns);
                if (report.Totals is { } totals) Assert.Equal(report.Columns.Count, totals.Count);
            }

            var asOf = await service.RunAsync(scope, "INV-07", query, Cancellation).ConfigureAwait(true);
            Assert.Contains(asOf.Notes, n => n.Contains("2026-12-31", StringComparison.Ordinal)); // the as-of date is stated, never silently the current state
            var fx = await Assert.ThrowsAsync<BusinessRuleException>(() => service.RunAsync(scope, "FX-07", query, Cancellation)).ConfigureAwait(true);
            Assert.Equal("report.no_fx_accounts", fx.Code);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task CashFlowShowsUnmappedBalanceSheetCounterAccountsAsUnclassifiedNotOperating()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var (s, cash, _, _) = await SeedAsync(database).ConfigureAwait(true);
            var loan = await s.AccountAsync(s.CompanyA, "2501", AccountTypeCode.Liabilities, true).ConfigureAwait(true);
            await s.JournalAsync(s.CompanyA, s.BranchA1, new DateOnly(2026, 4, 1), "USD", true, (cash.Id, 300m, 0m), (loan.Id, 0m, 300m)).ConfigureAwait(true);
            using var context = database.Context();
            context.BranchAccountLinks.Add(new PetraERP.Infrastructure.Organizations.BranchAccountLinkRow { Id = Guid.CreateVersion7(), CompanyId = s.CompanyA, BranchId = s.BranchA1, Role = 1, AccountId = cash.Id, EffectiveFrom = new DateOnly(2026, 1, 1), CreatedByUserId = s.Actor, CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow });
            await context.SaveChangesAsync(Cancellation).ConfigureAwait(true);
            var service = new SqlLedgerQueryService(context);
            var scope = LedgerReportScope.Create(s.CompanyA, s.BranchA1, s.Actor, false);
            var report = await service.RunAsync(scope, "FIN-03", new AnalyticalQuery(new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30), null, null), Cancellation).ConfigureAwait(true);
            Assert.Contains(report.Rows, r => r[0].Contains("غير مصنّف", StringComparison.Ordinal));
            Assert.DoesNotContain(report.Rows.SkipWhile(r => r[0] != "التشغيل").TakeWhile(r => !r[0].Contains("غير مصنّف", StringComparison.Ordinal)), r => r[1].Contains("2501", StringComparison.Ordinal));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task Batch4ReportsRunAndRoleDependentOnesFailExplicitlyWithoutMappings()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var (s, _, _, _) = await SeedAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var service = new SqlLedgerQueryService(context);
            var scope = LedgerReportScope.Create(s.CompanyA, s.BranchA1, s.Actor, true);
            var query = new AnalyticalQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), null, null);
            foreach (var id in new[] { "FIN-06", "FIN-07", "CC-03", "CC-04", "CC-06", "CC-07", "CC-08", "INV-10", "INV-16", "SALES-11", "BUY-07", "BUY-08", "ARAP-06", "ARAP-11", "GL-11", "ARAP-05", "ARAP-07", "ARAP-08", "CASH-05", "CASH-07", "INV-14" })
            {
                var report = await service.RunAsync(scope, id, query, Cancellation).ConfigureAwait(true);
                Assert.Equal(id, report.ReportId);
                if (report.Totals is { } totals) Assert.Equal(report.Columns.Count, totals.Count);
                foreach (var row in report.Rows) Assert.Equal(report.Columns.Count, row.Count);
            }

            foreach (var id in new[] { "FIN-05", "SALES-01", "BUY-01", "SALES-07", "BUY-05", "SALES-10", "CASH-06" })
            {
                var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.RunAsync(scope, id, query, Cancellation)).ConfigureAwait(true);
                Assert.StartsWith("report.no_", ex.Code, StringComparison.Ordinal);
            }
        }
    }
}
