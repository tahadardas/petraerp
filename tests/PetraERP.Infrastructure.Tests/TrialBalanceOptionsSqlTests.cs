using Microsoft.EntityFrameworkCore;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.Reports;
using PetraERP.Infrastructure.GeneralLedger;

namespace PetraERP.Infrastructure.Tests;

public sealed class TrialBalanceOptionsSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task HistoricalParentPostingIsIncludedOnceInTrialBalanceTotals()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            var parent = await scenario.AccountAsync(scenario.CompanyA, "10", AccountTypeCode.Assets, true).ConfigureAwait(true);
            var child = await scenario.AccountAsync(scenario.CompanyA, "1001", AccountTypeCode.Assets, true).ConfigureAwait(true);
            var credit = await scenario.AccountAsync(scenario.CompanyA, "40", AccountTypeCode.Revenue, true).ConfigureAwait(true);
            await scenario.JournalAsync(scenario.CompanyA, scenario.BranchA1, new DateOnly(2026, 2, 1), "USD", true,
                (parent.Id, 100m, 0m), (child.Id, 50m, 0m), (credit.Id, 0m, 150m)).ConfigureAwait(true);

            using var context = database.Context();
            // Reproduce history imported before the current rule forbidding children on a postable account.
            // Only this disposable, marker-owned test database is modified directly.
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE [ledger].[Accounts] SET [ParentId]={parent.Id} WHERE [Id]={child.Id}", Cancellation).ConfigureAwait(true);
            var scope = LedgerReportScope.Create(scenario.CompanyA, scenario.BranchA1, scenario.Actor, false);
            var report = await new SqlLedgerQueryService(context).GetTrialBalanceAsync(scope,
                new TrialBalanceQuery(null, new DateOnly(2026, 12, 31), null, null, false, null), Cancellation).ConfigureAwait(true);

            Assert.Equal(150m, report.Rows.Single(r => r.AccountId == parent.Id).EndingDebit);
            Assert.Equal(50m, report.Rows.Single(r => r.AccountId == child.Id).EndingDebit);
            Assert.Equal(150m, report.TotalEndingDebit);
            Assert.Equal(150m, report.TotalEndingCredit);
            Assert.True(report.IsBalanced);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task TrialBalanceParametersRestrictAndRestateTheReport()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var s = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            var assets = await s.AccountAsync(s.CompanyA, "1", AccountTypeCode.Assets, false).ConfigureAwait(true);
            var cash = await s.AccountAsync(s.CompanyA, "1101", AccountTypeCode.Assets, true, assets.Id).ConfigureAwait(true);
            var revenue = await s.AccountAsync(s.CompanyA, "4", AccountTypeCode.Revenue, false).ConfigureAwait(true);
            var sales = await s.AccountAsync(s.CompanyA, "4101", AccountTypeCode.Revenue, true, revenue.Id).ConfigureAwait(true);
            await s.JournalAsync(s.CompanyA, s.BranchA1, new DateOnly(2026, 1, 5), "USD", true, (cash.Id, 1000m, 0m), (sales.Id, 0m, 1000m)).ConfigureAwait(true);
            await s.JournalAsync(s.CompanyA, s.BranchA1, new DateOnly(2026, 2, 10), "USD", true, (cash.Id, 400m, 0m), (sales.Id, 0m, 400m)).ConfigureAwait(true);

            using var context = database.Context();
            var reports = new SqlLedgerQueryService(context);
            var scope = LedgerReportScope.Create(s.CompanyA, s.BranchA1, s.Actor, false);
            var end = new DateOnly(2026, 12, 31);
            Task<TrialBalanceDto> Run(TrialBalanceOptions options) =>
                reports.GetTrialBalanceAsync(scope, new TrialBalanceQuery(null, end, null, null, false, null) { Options = options }, Cancellation);

            var plain = await Run(new TrialBalanceOptions()).ConfigureAwait(true);
            Assert.Equal(1400m, plain.TotalEndingDebit);
            Assert.Equal(1m, plain.DisplayRate);

            // Account: the control account and what is below it only.
            var subtree = await Run(new TrialBalanceOptions { AccountId = assets.Id }).ConfigureAwait(true);
            Assert.Equal([assets.Id, cash.Id], subtree.Rows.Select(r => r.AccountId).ToArray());
            Assert.Equal(1400m, subtree.TotalEndingDebit);
            Assert.Equal(0m, subtree.TotalEndingCredit);
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => Run(new TrialBalanceOptions { AccountId = Guid.NewGuid() })).ConfigureAwait(true);

            // Account scope.
            var posting = await Run(new TrialBalanceOptions { AccountScope = TrialBalanceAccountScope.PostingOnly }).ConfigureAwait(true);
            Assert.All(posting.Rows, r => Assert.True(r.PostingAllowed));
            var parents = await Run(new TrialBalanceOptions { AccountScope = TrialBalanceAccountScope.ParentsOnly }).ConfigureAwait(true);
            Assert.All(parents.Rows, r => Assert.False(r.PostingAllowed));
            Assert.Equal(plain.TotalEndingDebit, parents.TotalEndingDebit);

            // Cost center: no line carries it, so the report is empty and still balanced.
            var empty = await Run(new TrialBalanceOptions { CostCenterId = Guid.NewGuid() }).ConfigureAwait(true);
            Assert.Empty(empty.Rows);
            Assert.True(empty.IsBalanced);

            // Journal number range: the first posted journal only.
            var numbers = await context.JournalEntries.AsNoTracking().Where(j => j.CompanyId == s.CompanyA && j.PostingSequence != null)
                .Select(j => j.PostingSequence!.Value).OrderBy(n => n).ToListAsync(Cancellation).ConfigureAwait(true);
            var first = await Run(new TrialBalanceOptions { FromJournalNumber = numbers[0], ToJournalNumber = numbers[0] }).ConfigureAwait(true);
            Assert.Equal(1000m, first.TotalEndingDebit);

            // Sources: both journals are plain journals, so a voucher-only report is empty and a journal-only one is complete.
            Assert.Empty((await Run(new TrialBalanceOptions { Sources = [TrialBalanceSources.Voucher] }).ConfigureAwait(true)).Rows);
            Assert.Equal(1400m, (await Run(new TrialBalanceOptions { Sources = [TrialBalanceSources.Journal] }).ConfigureAwait(true)).TotalEndingDebit);

            // Display currency: every amount is divided by the rate.
            var restated = await Run(new TrialBalanceOptions { DisplayCurrencyCode = "EUR", DisplayRate = 2m }).ConfigureAwait(true);
            Assert.Equal(700m, restated.TotalEndingDebit);
            Assert.Equal("EUR", restated.DisplayCurrencyCode);
            Assert.Equal(700m, restated.Rows.Single(r => r.AccountId == cash.Id).EndingDebit);
            var parityRate = await Run(new TrialBalanceOptions { DisplayCurrencyCode = "EUR", DisplayRate = 1m }).ConfigureAwait(true);
            Assert.Equal("EUR", parityRate.DisplayCurrencyCode);
            Assert.Equal(1400m, parityRate.TotalEndingDebit);
            await Assert.ThrowsAsync<BusinessRuleException>(() => Run(new TrialBalanceOptions { DisplayCurrencyCode = "EUR", DisplayRate = 0m })).ConfigureAwait(true);

            // Last movement dates, rolled up to the control account.
            var last = await Run(new TrialBalanceOptions { ShowLastMovement = true }).ConfigureAwait(true);
            Assert.Equal(new DateOnly(2026, 2, 10), last.Rows.Single(r => r.AccountId == cash.Id).LastDebitDate);
            Assert.Null(last.Rows.Single(r => r.AccountId == cash.Id).LastCreditDate);
            Assert.Equal(new DateOnly(2026, 2, 10), last.Rows.Single(r => r.AccountId == assets.Id).LastDebitDate);
            Assert.Equal(new DateOnly(2026, 2, 10), last.Rows.Single(r => r.AccountId == sales.Id).LastCreditDate);
        }
    }
}
