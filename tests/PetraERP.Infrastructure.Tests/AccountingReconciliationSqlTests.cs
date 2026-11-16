#pragma warning disable CA1707

using Microsoft.EntityFrameworkCore;
using PetraERP.Application.GeneralLedger.Reconciliation;
using PetraERP.Infrastructure.GeneralLedger.Reconciliation;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Tests;

public sealed class AccountingReconciliationSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task Reconciliation_RunsAll14Checks_ExportsJsonAndCsv()
    {
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            var provisioned = await fixture.ProvisionAsync().ConfigureAwait(true);
            Assert.True(provisioned.Succeeded, provisioned.Error);

            using var context = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true);
            var company = await context.Companies.SingleAsync(Cancellation).ConfigureAwait(true);
            var branch = await context.Branches.SingleAsync(Cancellation).ConfigureAwait(true);

            var service = new SqlAccountingReconciliationService(context, TimeProvider.System);

            // 1. Run all 14 reconciliations on clean database
            var report = await service.RunAllReconciliationsAsync(company.Id, provisioned.FiscalYearId, branch.Id, Cancellation).ConfigureAwait(true);
            Assert.NotNull(report);
            Assert.Equal(15, report.TotalChecks);
            Assert.False(report.HasBlockingDifferences);
            Assert.Equal(0, report.BlockingChecks);

            // Verify checks 1 through 14 exist
            var checkCodes = report.Findings.Select(f => f.CheckCode).ToList();
            Assert.Contains("CHECK_01_TRIAL_BALANCE", checkCodes);
            Assert.Contains("CHECK_02_GL_VS_JOURNAL_LINES", checkCodes);
            Assert.Contains("CHECK_03_AR_SUBLEDGER_VS_CONTROL", checkCodes);
            Assert.Contains("CHECK_04_AP_SUBLEDGER_VS_CONTROL", checkCodes);
            Assert.Contains("CHECK_05_TREASURY_VS_GL", checkCodes);
            Assert.Contains("CHECK_06_INVENTORY_VS_GL", checkCodes);
            Assert.Contains("CHECK_07_SALES_DOCS_VS_POSTING", checkCodes);
            Assert.Contains("CHECK_08_PURCHASE_DOCS_VS_POSTING", checkCodes);
            Assert.Contains("CHECK_09_SETTLEMENT_ALLOCATIONS", checkCodes);
            Assert.Contains("CHECK_10_PERIOD_CONTINUITY", checkCodes);
            Assert.Contains("CHECK_11_YEAR_ROLLOVER_CONTINUITY", checkCodes);
            Assert.Contains("CHECK_12_FX_EXPOSURE_REVALUATION", checkCodes);
            Assert.Contains("CHECK_13_DOC_STATE_VS_POSTING_STATE", checkCodes);
            Assert.Contains("CHECK_14_DUPLICATE_OR_ORPHAN_POSTING", checkCodes);
            Assert.Contains("CHECK_15_DUPLICATE_DRAFTS", checkCodes);

            // 2. Export to JSON
            var json = await service.ExportReportToJsonAsync(company.Id, provisioned.FiscalYearId, branch.Id, Cancellation).ConfigureAwait(true);
            Assert.False(string.IsNullOrWhiteSpace(json));
            Assert.Contains("CHECK_01_TRIAL_BALANCE", json);

            // 3. Export to CSV
            var csv = await service.ExportReportToCsvAsync(company.Id, provisioned.FiscalYearId, branch.Id, Cancellation).ConfigureAwait(true);
            Assert.False(string.IsNullOrWhiteSpace(csv));
            Assert.StartsWith("CheckCode,Severity,Difference,SourceAmount,CalculatedAmount,Explanation,SuggestedAction", csv);
            Assert.Contains("CHECK_01_TRIAL_BALANCE", csv);
        }
    }

    /// <summary>
    /// A finding is stamped with the fiscal year under review, so it must be computed from that year's journals. An imbalance booked
    /// outside the year must not block it, and the same imbalance inside the year must. Without the scope, every year inherits the
    /// whole history: the operator is blocked for a problem that belongs to another year, or told the year is clean when it is not.
    /// </summary>
    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task TrialBalanceAndJournalLineChecksFollowTheFiscalYearRange()
    {
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            var provisioned = await fixture.ProvisionAsync().ConfigureAwait(true);
            Assert.True(provisioned.Succeeded, provisioned.Error);
            using var context = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true);
            var company = await context.Companies.SingleAsync(Cancellation).ConfigureAwait(true);
            var branch = await context.Branches.SingleAsync(Cancellation).ConfigureAwait(true);
            var year = await context.FiscalYears.SingleAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(provisioned.FiscalYearId, year.Id);
            var periodId = await context.AccountingPeriods.Where(p => p.CompanyId == company.Id)
                .OrderBy(p => p.RangeStart).Select(p => p.Id).FirstAsync(Cancellation).ConfigureAwait(true);
            // The trial balance check sums posted journal lines; the account it lands on is irrelevant to the arithmetic.
            var account = await context.Accounts.OrderBy(a => a.Code).FirstOrDefaultAsync(Cancellation).ConfigureAwait(true);
            Assert.NotNull(account);
            var service = new SqlAccountingReconciliationService(context, TimeProvider.System);
            string Check(string code, ReconciliationReportDto report) => report.Findings.Single(f => f.CheckCode == code).Severity;

            await PostUnbalancedJournalAsync(context, company.Id, branch.Id, account!.Id, company.BaseCurrencyCode,
                year.Id, periodId, 9001, year.RangeStart.AddDays(-1)).ConfigureAwait(true);
            var outside = await service.RunAllReconciliationsAsync(company.Id, provisioned.FiscalYearId, branch.Id, Cancellation).ConfigureAwait(true);
            Assert.Equal("PASS", Check("CHECK_01_TRIAL_BALANCE", outside));
            Assert.Equal("PASS", Check("CHECK_02_GL_VS_JOURNAL_LINES", outside));
            Assert.Equal(0m, outside.Findings.Single(f => f.CheckCode == "CHECK_01_TRIAL_BALANCE").Difference);

            await PostUnbalancedJournalAsync(context, company.Id, branch.Id, account!.Id, company.BaseCurrencyCode,
                year.Id, periodId, 9002, year.RangeStart).ConfigureAwait(true);
            var inside = await service.RunAllReconciliationsAsync(company.Id, provisioned.FiscalYearId, branch.Id, Cancellation).ConfigureAwait(true);
            Assert.Equal("BLOCKING", Check("CHECK_01_TRIAL_BALANCE", inside));
            Assert.Equal("BLOCKING", Check("CHECK_02_GL_VS_JOURNAL_LINES", inside));
            Assert.Equal(100m, inside.Findings.Single(f => f.CheckCode == "CHECK_01_TRIAL_BALANCE").Difference);

            // An imbalance dated after the year must not be counted in it either, so the answer stays at the one inside the range.
            await PostUnbalancedJournalAsync(context, company.Id, branch.Id, account!.Id, company.BaseCurrencyCode,
                year.Id, periodId, 9003, year.RangeEnd.AddDays(1)).ConfigureAwait(true);
            var after = await service.RunAllReconciliationsAsync(company.Id, provisioned.FiscalYearId, branch.Id, Cancellation).ConfigureAwait(true);
            Assert.Equal(100m, after.Findings.Single(f => f.CheckCode == "CHECK_01_TRIAL_BALANCE").Difference);
        }
    }

    /// <summary>
    /// A posted journal whose lines deliberately do not balance. The posting columns are filled because the ledger's own check
    /// constraints forbid a posted entry without a sequence, fiscal year and period.
    /// </summary>
    private static async Task PostUnbalancedJournalAsync(AccountingDbContext context, Guid companyId, Guid branchId, Guid accountId,
        string baseCurrency, Guid fiscalYearId, Guid periodId, long sequence, DateOnly accountingDate)
    {
        var now = DateTimeOffset.UtcNow;
        var journal = Guid.CreateVersion7();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO [ledger].[JournalEntries]([Id],[CompanyId],[BranchId],[AccountingDate],[Description],[State],[PostingSequence],[PostingFiscalYearId],[PostingPeriodId],[CreatedAtUtc],[UpdatedAtUtc])
            VALUES({journal},{companyId},{branchId},{accountingDate},N'قيد اختبار نطاق السنة',4,{sequence},{fiscalYearId},{periodId},{now},{now});
            """, Cancellation).ConfigureAwait(true);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO [ledger].[JournalLines]([Id],[CompanyId],[JournalId],[AccountId],[TransactionCurrencyCode],[TransactionDebit],[TransactionCredit],[BaseCurrencyCode],[BaseDebit],[BaseCredit],[RateSourceCurrencyCode],[RateTargetCurrencyCode],[RateValue],[Rounding],[Description],[CreatedAtUtc],[UpdatedAtUtc])
               VALUES({Guid.CreateVersion7()},{companyId},{journal},{accountId},{baseCurrency},100,0,{baseCurrency},100,0,{baseCurrency},{baseCurrency},1.0,0,N'مدين فقط',{now},{now});", Cancellation).ConfigureAwait(true);
    }
}
