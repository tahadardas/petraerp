using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Infrastructure.GeneralLedger;

namespace PetraERP.Infrastructure.Tests;

public sealed class JournalVoucherHeaderSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task DailyVoucherMarkerNamesTheCashAccountAndDiesWithItsDraft()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var s = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            var cash = await s.AccountAsync(s.CompanyA, "1101", AccountTypeCode.Assets, true).ConfigureAwait(true);
            var sales = await s.AccountAsync(s.CompanyA, "4101", AccountTypeCode.Revenue, true).ConfigureAwait(true);
            var parent = await s.AccountAsync(s.CompanyA, "1", AccountTypeCode.Assets, false).ConfigureAwait(true);
            var otherCash = await s.AccountAsync(s.CompanyB, "1101", AccountTypeCode.Assets, true).ConfigureAwait(true);
            var draft = await s.JournalAsync(s.CompanyA, s.BranchA1, new DateOnly(2026, 3, 1), "USD", false, (cash.Id, 100m, 0m), (sales.Id, 0m, 100m)).ConfigureAwait(true);
            var scope = JournalEntryScope.Create(s.CompanyA, s.BranchA1, s.Actor);

            using (var context = database.Context())
            {
                var service = new SqlJournalVoucherHeaderService(context, TimeProvider.System);
                Assert.Null(await service.GetAsync(scope, draft.Id, Cancellation).ConfigureAwait(true));
                var set = await service.SetAsync(scope, draft.Id, new JournalVoucherHeaderData(JournalVoucherHeaderData.Daily, cash.Id), Cancellation).ConfigureAwait(true);
                Assert.Equal(cash.Id, set.CashAccountId);
                // Replacing the marker keeps one row.
                var again = await service.SetAsync(scope, draft.Id, new JournalVoucherHeaderData(JournalVoucherHeaderData.Daily, sales.Id), Cancellation).ConfigureAwait(true);
                Assert.Equal(sales.Id, again.CashAccountId);
                await Assert.ThrowsAsync<BusinessRuleException>(() => service.SetAsync(scope, draft.Id, new JournalVoucherHeaderData(JournalVoucherHeaderData.Daily, parent.Id), Cancellation)).ConfigureAwait(true);
                await Assert.ThrowsAsync<BusinessRuleException>(() => service.SetAsync(scope, draft.Id, new JournalVoucherHeaderData(JournalVoucherHeaderData.Daily, otherCash.Id), Cancellation)).ConfigureAwait(true);
                await Assert.ThrowsAsync<RequestValidationException>(() => service.SetAsync(scope, draft.Id, new JournalVoucherHeaderData("OTHER", cash.Id), Cancellation)).ConfigureAwait(true);
                await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.GetAsync(scope, Guid.NewGuid(), Cancellation)).ConfigureAwait(true);
            }

            // A draft is deleted together with its marker; the account row is untouched.
            using (var context = database.Context())
            {
                var journals = new SqlJournalEntryService(context, TimeProvider.System);
                var current = await journals.GetJournalAsync(scope, draft.Id, Cancellation).ConfigureAwait(true);
                await journals.DeleteDraftAsync(scope, draft.Id, LedgerScenario.Token(current.VersionBase64), Cancellation).ConfigureAwait(true);
            }

            using (var context = database.Context())
            {
                var service = new SqlJournalVoucherHeaderService(context, TimeProvider.System);
                await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.GetAsync(scope, draft.Id, Cancellation)).ConfigureAwait(true);
            }

            // Once posted, the marker is frozen.
            var posted = await s.JournalAsync(s.CompanyA, s.BranchA1, new DateOnly(2026, 3, 2), "USD", true, (cash.Id, 50m, 0m), (sales.Id, 0m, 50m)).ConfigureAwait(true);
            using (var context = database.Context())
            {
                var service = new SqlJournalVoucherHeaderService(context, TimeProvider.System);
                await Assert.ThrowsAsync<BusinessRuleException>(() => service.SetAsync(scope, posted.Id, new JournalVoucherHeaderData(JournalVoucherHeaderData.Daily, cash.Id), Cancellation)).ConfigureAwait(true);
            }
        }
    }
}
