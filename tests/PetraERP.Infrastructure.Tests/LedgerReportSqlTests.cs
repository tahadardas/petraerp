using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Application.GeneralLedger.Reports;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Infrastructure.GeneralLedger;

namespace PetraERP.Infrastructure.Tests;

public sealed class LedgerReportSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private sealed record Chart(AccountDto Assets, AccountDto Cash, AccountDto Bank, AccountDto Revenue, AccountDto Sales, AccountDto Expense);

    /// <summary>
    /// A1: Jan 5 cash 1000 / sales 1000; Feb 10 expense 200 / cash 200; Mar 1 a draft (never reported).
    /// A2: Jan 20 bank 500 / sales 500. Company B: an unrelated posted journal.
    /// </summary>
    private static async Task<(LedgerScenario Scenario, Chart Chart)> SeedAsync(SqlTestDatabase database)
    {
        var s = await LedgerScenario.CreateAsync(database).ConfigureAwait(false);
        var assets = await s.AccountAsync(s.CompanyA, "1", AccountTypeCode.Assets, false).ConfigureAwait(false);
        var cash = await s.AccountAsync(s.CompanyA, "1101", AccountTypeCode.Assets, true, assets.Id).ConfigureAwait(false);
        var bank = await s.AccountAsync(s.CompanyA, "1102", AccountTypeCode.Assets, true, assets.Id).ConfigureAwait(false);
        var revenue = await s.AccountAsync(s.CompanyA, "4", AccountTypeCode.Revenue, false).ConfigureAwait(false);
        var sales = await s.AccountAsync(s.CompanyA, "4101", AccountTypeCode.Revenue, true, revenue.Id).ConfigureAwait(false);
        var expense = await s.AccountAsync(s.CompanyA, "5101", AccountTypeCode.Expenses, true).ConfigureAwait(false);
        await s.JournalAsync(s.CompanyA, s.BranchA1, new DateOnly(2026, 1, 5), "USD", true, (cash.Id, 1000m, 0m), (sales.Id, 0m, 1000m)).ConfigureAwait(false);
        await s.JournalAsync(s.CompanyA, s.BranchA1, new DateOnly(2026, 2, 10), "USD", true, (expense.Id, 200m, 0m), (cash.Id, 0m, 200m)).ConfigureAwait(false);
        await s.JournalAsync(s.CompanyA, s.BranchA1, new DateOnly(2026, 3, 1), "USD", false, (cash.Id, 9999m, 0m), (sales.Id, 0m, 9999m)).ConfigureAwait(false);
        await s.JournalAsync(s.CompanyA, s.BranchA2, new DateOnly(2026, 1, 20), "USD", true, (bank.Id, 500m, 0m), (sales.Id, 0m, 500m)).ConfigureAwait(false);
        var otherCash = await s.AccountAsync(s.CompanyB, "1101", AccountTypeCode.Assets, true).ConfigureAwait(false);
        var otherSales = await s.AccountAsync(s.CompanyB, "4101", AccountTypeCode.Revenue, true).ConfigureAwait(false);
        await s.JournalAsync(s.CompanyB, s.BranchB1, new DateOnly(2026, 1, 7), "SYP", true, (otherCash.Id, 777m, 0m), (otherSales.Id, 0m, 777m)).ConfigureAwait(false);
        return (s, new Chart(assets, cash, bank, revenue, sales, expense));
    }

    private static LedgerReportScope Scope(LedgerScenario s, Guid company, Guid branch, bool consolidated = false) =>
        LedgerReportScope.Create(company, branch, s.Actor, consolidated);

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task GeneralJournalReconcilesAndRespectsStateBranchAndCompanyScope()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var (s, chart) = await SeedAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var reports = new SqlLedgerQueryService(context);
            var all = PageRequest.Create(1, 200);

            var posted = await reports.GetGeneralJournalAsync(Scope(s, s.CompanyA, s.BranchA1), new GeneralJournalQuery(null, null, null, null, null, null, all), Cancellation).ConfigureAwait(true);
            Assert.Equal(4, posted.TotalCount);
            Assert.Equal(1200m, posted.TotalBaseDebit);
            Assert.Equal(posted.TotalBaseDebit, posted.TotalBaseCredit);
            Assert.Equal(posted.TotalBaseDebit, posted.Lines.Sum(l => l.BaseDebit));
            Assert.Equal("USD", posted.BaseCurrencyCode);
            Assert.All(posted.Lines, l => Assert.Equal(s.BranchA1, l.BranchId));
            Assert.All(posted.Lines, l => Assert.Equal((byte)JournalEntryState.Posted, l.State));
            Assert.DoesNotContain(posted.Lines, l => l.BaseDebit == 9999m);
            Assert.Equal(posted.Lines.OrderBy(l => l.AccountingDate).ThenBy(l => l.PostingSequence).Select(l => l.LineId), posted.Lines.Select(l => l.LineId));

            var drafts = await reports.GetGeneralJournalAsync(Scope(s, s.CompanyA, s.BranchA1),
                new GeneralJournalQuery(null, null, null, null, [(byte)JournalEntryState.Draft], null, all), Cancellation).ConfigureAwait(true);
            Assert.Equal(2, drafts.TotalCount);

            var cashOnly = await reports.GetGeneralJournalAsync(Scope(s, s.CompanyA, s.BranchA1),
                new GeneralJournalQuery(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28), chart.Cash.Id, null, null, null, all), Cancellation).ConfigureAwait(true);
            Assert.Equal(200m, Assert.Single(cashOnly.Lines).BaseCredit);

            var bySequence = await reports.GetGeneralJournalAsync(Scope(s, s.CompanyA, s.BranchA1),
                new GeneralJournalQuery(null, null, null, null, null, "2", all), Cancellation).ConfigureAwait(true);
            Assert.Equal(2, bySequence.Lines.Count(l => l.PostingSequence == 2)); // number match (descriptions may also contain the digits)
            var byText = await reports.GetGeneralJournalAsync(Scope(s, s.CompanyA, s.BranchA1),
                new GeneralJournalQuery(null, null, null, null, null, "2026-02-10", all), Cancellation).ConfigureAwait(true);
            Assert.Equal(2, byText.TotalCount);
            Assert.All(byText.Lines, l => Assert.Equal(new DateOnly(2026, 2, 10), l.AccountingDate));

            var paged = await reports.GetGeneralJournalAsync(Scope(s, s.CompanyA, s.BranchA1), new GeneralJournalQuery(null, null, null, null, null, null, PageRequest.Create(2, 3)), Cancellation).ConfigureAwait(true);
            Assert.Single(paged.Lines);
            Assert.Equal(4, paged.TotalCount);

            var consolidated = await reports.GetGeneralJournalAsync(Scope(s, s.CompanyA, s.BranchA1, consolidated: true), new GeneralJournalQuery(null, null, null, null, null, null, all), Cancellation).ConfigureAwait(true);
            Assert.Equal(1700m, consolidated.TotalBaseDebit);
            Assert.Equal(consolidated.TotalBaseDebit, consolidated.TotalBaseCredit);
            Assert.DoesNotContain(consolidated.Lines, l => l.BaseDebit == 777m);

            var otherCompany = await reports.GetGeneralJournalAsync(Scope(s, s.CompanyB, s.BranchB1), new GeneralJournalQuery(null, null, null, null, null, null, all), Cancellation).ConfigureAwait(true);
            Assert.Equal(777m, otherCompany.TotalBaseDebit);
            Assert.Equal("SYP", otherCompany.BaseCurrencyCode);
            Assert.DoesNotContain(otherCompany.Lines, l => l.AccountId == chart.Cash.Id);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task AccountLedgerCarriesOpeningBalanceAndRunningBalanceAcrossPages()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var (s, chart) = await SeedAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var reports = new SqlLedgerQueryService(context);

            var february = await reports.GetAccountLedgerAsync(Scope(s, s.CompanyA, s.BranchA1),
                new AccountLedgerQuery(chart.Cash.Id, new DateOnly(2026, 2, 1), new DateOnly(2026, 12, 31), PageRequest.Create(1, 50)), Cancellation).ConfigureAwait(true);
            Assert.Equal(1000m, february.OpeningBalance);
            Assert.Equal(0m, february.PeriodDebit);
            Assert.Equal(200m, february.PeriodCredit);
            Assert.Equal(800m, february.ClosingBalance);
            Assert.Equal(800m, Assert.Single(february.Lines).RunningBalance);

            var secondPage = await reports.GetAccountLedgerAsync(Scope(s, s.CompanyA, s.BranchA1),
                new AccountLedgerQuery(chart.Cash.Id, null, null, PageRequest.Create(2, 1)), Cancellation).ConfigureAwait(true);
            Assert.Equal(0m, secondPage.OpeningBalance);
            Assert.Equal(2, secondPage.TotalCount);
            Assert.Equal(800m, Assert.Single(secondPage.Lines).RunningBalance);
            Assert.Equal(secondPage.ClosingBalance, secondPage.Lines[^1].RunningBalance);

            var control = await reports.GetAccountLedgerAsync(Scope(s, s.CompanyA, s.BranchA1, consolidated: true),
                new AccountLedgerQuery(chart.Assets.Id, null, null, PageRequest.Create(1, 50)), Cancellation).ConfigureAwait(true);
            Assert.Equal(3, control.IncludedAccountCount);
            Assert.Equal(1300m, control.ClosingBalance); // cash 800 + bank 500
            Assert.Equal(control.ClosingBalance, control.Lines[^1].RunningBalance);

            await Assert.ThrowsAsync<ResourceNotFoundException>(() => reports.GetAccountLedgerAsync(Scope(s, s.CompanyB, s.BranchB1),
                new AccountLedgerQuery(chart.Cash.Id, null, null, PageRequest.Create(1, 50)), Cancellation)).ConfigureAwait(true);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task TrialBalanceDebitsEqualCreditsAndRollsUpTheHierarchy()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var (s, chart) = await SeedAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var reports = new SqlLedgerQueryService(context);

            var tb = await reports.GetTrialBalanceAsync(Scope(s, s.CompanyA, s.BranchA1, consolidated: true),
                new TrialBalanceQuery(new DateOnly(2026, 2, 1), new DateOnly(2026, 12, 31), null, null, false, null), Cancellation).ConfigureAwait(true);
            Assert.True(tb.IsBalanced);
            Assert.Equal(1500m, tb.TotalOpeningDebit); // cash 1000 + bank 500
            Assert.Equal(tb.TotalOpeningDebit, tb.TotalOpeningCredit);
            Assert.Equal(200m, tb.TotalPeriodDebit);
            Assert.Equal(tb.TotalPeriodDebit, tb.TotalPeriodCredit);
            Assert.Equal(tb.TotalEndingDebit, tb.TotalEndingCredit);
            Assert.Equal(1500m, tb.TotalEndingDebit); // cash 800 + bank 500 + expense 200
            var assets = tb.Rows.Single(r => r.AccountId == chart.Assets.Id);
            Assert.Equal(0, assets.Level);
            Assert.Equal(1300m, assets.EndingDebit);
            Assert.Equal(1500m, tb.Rows.Single(r => r.AccountId == chart.Revenue.Id).EndingCredit);
            Assert.Equal(1, tb.Rows.Single(r => r.AccountId == chart.Cash.Id).Level);
            Assert.Equal(tb.TotalEndingDebit, tb.Rows.Where(r => r.PostingAllowed).Sum(r => r.EndingDebit));

            var january = (await new PetraERP.Infrastructure.AccountingCalendars.SqlAccountingCalendarService(context, TimeProvider.System)
                .ListFiscalYearsAsync(s.Master(s.CompanyA), Cancellation).ConfigureAwait(true))[0].Periods[0];
            var branchOnly = await reports.GetTrialBalanceAsync(Scope(s, s.CompanyA, s.BranchA1),
                new TrialBalanceQuery(null, DateOnly.MaxValue, january.Id, null, false, null), Cancellation).ConfigureAwait(true);
            Assert.True(branchOnly.IsBalanced);
            Assert.Equal(1000m, branchOnly.TotalPeriodDebit);
            Assert.Equal(new DateOnly(2026, 1, 31), branchOnly.To);
            Assert.DoesNotContain(branchOnly.Rows, r => r.AccountId == chart.Bank.Id);

            var topLevel = await reports.GetTrialBalanceAsync(Scope(s, s.CompanyA, s.BranchA1, consolidated: true),
                new TrialBalanceQuery(null, new DateOnly(2026, 12, 31), null, null, false, 0), Cancellation).ConfigureAwait(true);
            Assert.All(topLevel.Rows, r => Assert.Equal(0, r.Level));
            Assert.Equal(tb.TotalEndingDebit, topLevel.TotalEndingDebit);

            var other = await reports.GetTrialBalanceAsync(Scope(s, s.CompanyB, s.BranchB1),
                new TrialBalanceQuery(null, new DateOnly(2026, 12, 31), null, null, false, null), Cancellation).ConfigureAwait(true);
            Assert.Equal(777m, other.TotalEndingDebit);
            Assert.DoesNotContain(other.Rows, r => r.AccountId == chart.Cash.Id);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ConsolidationRequiresEveryBranchGrantAndReversalsNetToZero()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var (s, chart) = await SeedAsync(database).ConfigureAwait(true);
            var wrong = await s.JournalAsync(s.CompanyA, s.BranchA1, new DateOnly(2026, 4, 1), "USD", true, (chart.Cash.Id, 300m, 0m), (chart.Sales.Id, 0m, 300m)).ConfigureAwait(true);
            await s.ReverseAsync(s.CompanyA, s.BranchA1, wrong, new DateOnly(2026, 4, 2)).ConfigureAwait(true);

            using (var context = database.Context())
            {
                var reports = new SqlLedgerQueryService(context);
                var ledger = await reports.GetAccountLedgerAsync(Scope(s, s.CompanyA, s.BranchA1),
                    new AccountLedgerQuery(chart.Cash.Id, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30), PageRequest.Create(1, 50)), Cancellation).ConfigureAwait(true);
                Assert.Equal(2, ledger.TotalCount); // original (now Reversed) and its posted reversal
                Assert.Equal(ledger.OpeningBalance, ledger.ClosingBalance);
                var tb = await reports.GetTrialBalanceAsync(Scope(s, s.CompanyA, s.BranchA1),
                    new TrialBalanceQuery(null, new DateOnly(2026, 12, 31), null, null, false, null), Cancellation).ConfigureAwait(true);
                Assert.True(tb.IsBalanced);
                Assert.Equal(800m, tb.Rows.Single(r => r.AccountId == chart.Cash.Id).EndingDebit);
            }

            await s.RevokeBranchAsync(s.BranchA2).ConfigureAwait(true);
            using (var context = database.Context())
            {
                var reports = new SqlLedgerQueryService(context);
                await Assert.ThrowsAsync<ReportScopeDeniedException>(() => reports.GetTrialBalanceAsync(Scope(s, s.CompanyA, s.BranchA1, consolidated: true),
                    new TrialBalanceQuery(null, new DateOnly(2026, 12, 31), null, null, false, null), Cancellation)).ConfigureAwait(true);
                await Assert.ThrowsAsync<ReportScopeDeniedException>(() => reports.GetGeneralJournalAsync(Scope(s, s.CompanyA, s.BranchA1, consolidated: true),
                    new GeneralJournalQuery(null, null, null, null, null, null, PageRequest.Create(1, 10)), Cancellation)).ConfigureAwait(true);
                var branchOnly = await reports.GetTrialBalanceAsync(Scope(s, s.CompanyA, s.BranchA1),
                    new TrialBalanceQuery(null, new DateOnly(2026, 12, 31), null, null, false, null), Cancellation).ConfigureAwait(true);
                Assert.True(branchOnly.IsBalanced);
            }
        }
    }
}
