using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Application.Treasury;
using PetraERP.Infrastructure.Treasury;

namespace PetraERP.Infrastructure.Tests;

public sealed class BankReconciliationSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ReconciliationClearsLinesCompletesOnlyAtZeroAndCarriesForwardWithoutReuse()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var s = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            var bank = await s.AccountAsync(s.CompanyA, "1102", AccountTypeCode.Assets, true).ConfigureAwait(true);
            var sales = await s.AccountAsync(s.CompanyA, "4101", AccountTypeCode.Revenue, true).ConfigureAwait(true);
            await s.JournalAsync(s.CompanyA, s.BranchA1, new DateOnly(2026, 1, 5), "USD", true, (bank.Id, 1000m, 0m), (sales.Id, 0m, 1000m)).ConfigureAwait(true);
            await s.JournalAsync(s.CompanyA, s.BranchA1, new DateOnly(2026, 1, 20), "USD", true, (sales.Id, 300m, 0m), (bank.Id, 0m, 300m)).ConfigureAwait(true);
            await s.JournalAsync(s.CompanyA, s.BranchA1, new DateOnly(2026, 2, 10), "USD", true, (bank.Id, 500m, 0m), (sales.Id, 0m, 500m)).ConfigureAwait(true);
            await s.JournalAsync(s.CompanyA, s.BranchA1, new DateOnly(2026, 1, 25), "USD", false, (bank.Id, 9999m, 0m), (sales.Id, 0m, 9999m)).ConfigureAwait(true);
            await s.JournalAsync(s.CompanyA, s.BranchA2, new DateOnly(2026, 1, 6), "USD", true, (bank.Id, 77m, 0m), (sales.Id, 0m, 77m)).ConfigureAwait(true);

            using var context = database.Context();
            var year = await context.FiscalYears.AsNoTracking().Where(y => y.CompanyId == s.CompanyA).Select(y => y.Id).SingleAsync(Cancellation).ConfigureAwait(true);
            var service = new SqlBankReconciliationService(context);
            Task<BankReconciliationDto> Start(DateOnly date, decimal balance) =>
                service.StartAsync(s.CompanyA, year, s.BranchA1, s.Actor, bank.Id, date, balance, Cancellation);

            // January: only the two posted branch-A1 lines up to Jan 31 are candidates (no draft, no other branch, no February).
            var january = await Start(new DateOnly(2026, 1, 31), 700m).ConfigureAwait(true);
            Assert.Equal(2, january.Lines.Count);
            Assert.Equal(700m, january.Difference);
            Assert.Equal(700m, january.BookBalanceAtStatementDate);
            await Assert.ThrowsAsync<BusinessRuleException>(() => Start(new DateOnly(2026, 1, 31), 700m)).ConfigureAwait(true);

            var first = january.Lines[0].JournalLineId;
            var second = january.Lines[1].JournalLineId;
            var partial = await service.SetClearedAsync(s.CompanyA, year, s.BranchA1, s.Actor, january.Id, [first], true, january.VersionBase64, Cancellation).ConfigureAwait(true);
            Assert.Equal(700m - 1000m, partial.Difference);
            // Completion is refused while the statement and the cleared lines disagree.
            var refusal = await Assert.ThrowsAsync<BusinessRuleException>(() => service.CompleteAsync(s.CompanyA, year, s.BranchA1, s.Actor, january.Id, partial.VersionBase64, Cancellation)).ConfigureAwait(true);
            Assert.Equal("bank_reconciliation.difference_not_zero", refusal.Code);
            // A stale version is a concurrency conflict, not a silent overwrite.
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => service.SetClearedAsync(s.CompanyA, year, s.BranchA1, s.Actor, january.Id, [second], true, january.VersionBase64, Cancellation)).ConfigureAwait(true);
            // A line that is not a candidate (unknown id) rejects the whole request.
            await Assert.ThrowsAsync<BusinessRuleException>(() => service.SetClearedAsync(s.CompanyA, year, s.BranchA1, s.Actor, january.Id, [second, Guid.NewGuid()], true, partial.VersionBase64, Cancellation)).ConfigureAwait(true);

            var cleared = await service.SetClearedAsync(s.CompanyA, year, s.BranchA1, s.Actor, january.Id, [second], true, partial.VersionBase64, Cancellation).ConfigureAwait(true);
            Assert.Equal(0m, cleared.Difference);
            var done = await service.CompleteAsync(s.CompanyA, year, s.BranchA1, s.Actor, january.Id, cleared.VersionBase64, Cancellation).ConfigureAwait(true);
            Assert.Equal(BankReconciliationStatus.Completed, done.Status);
            await Assert.ThrowsAsync<BusinessRuleException>(() => service.SetClearedAsync(s.CompanyA, year, s.BranchA1, s.Actor, january.Id, [first], false, done.VersionBase64, Cancellation)).ConfigureAwait(true);
            await Assert.ThrowsAsync<BusinessRuleException>(() => service.DiscardAsync(s.CompanyA, year, s.BranchA1, s.Actor, january.Id, done.VersionBase64, Cancellation)).ConfigureAwait(true);

            // The next statement must be later, starts with what January cleared, and offers only what is still open.
            await Assert.ThrowsAsync<BusinessRuleException>(() => Start(new DateOnly(2026, 1, 31), 700m)).ConfigureAwait(true);
            var february = await Start(new DateOnly(2026, 2, 28), 1200m).ConfigureAwait(true);
            Assert.Equal(700m, february.PriorClearedBalance);
            var open = Assert.Single(february.Lines);
            Assert.Equal(500m, open.Amount);
            Assert.Equal(500m, february.Difference);
            // A January line is already cleared and cannot be taken again.
            await Assert.ThrowsAsync<BusinessRuleException>(() => service.SetClearedAsync(s.CompanyA, year, s.BranchA1, s.Actor, february.Id, [first], true, february.VersionBase64, Cancellation)).ConfigureAwait(true);
            var febDone = await service.SetClearedAsync(s.CompanyA, year, s.BranchA1, s.Actor, february.Id, [open.JournalLineId], true, february.VersionBase64, Cancellation).ConfigureAwait(true);
            Assert.Equal(0m, febDone.Difference);
            var completed = await service.CompleteAsync(s.CompanyA, year, s.BranchA1, s.Actor, february.Id, febDone.VersionBase64, Cancellation).ConfigureAwait(true);
            Assert.Equal(BankReconciliationStatus.Completed, completed.Status);
            Assert.Equal(2, (await service.ListAsync(s.CompanyA, s.BranchA1, bank.Id, Cancellation).ConfigureAwait(true)).Count);

            // Discarding a draft releases its lines for a new reconciliation.
            var march = await Start(new DateOnly(2026, 3, 31), 1200m).ConfigureAwait(true);
            Assert.Empty(march.Lines);
            await service.DiscardAsync(s.CompanyA, year, s.BranchA1, s.Actor, march.Id, march.VersionBase64, Cancellation).ConfigureAwait(true);
            Assert.Equal(2, (await service.ListAsync(s.CompanyA, s.BranchA1, bank.Id, Cancellation).ConfigureAwait(true)).Count);
        }
    }
}
