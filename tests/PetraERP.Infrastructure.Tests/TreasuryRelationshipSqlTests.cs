using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Treasury;
using PetraERP.Domain.Treasury;

namespace PetraERP.Infrastructure.Tests;

public sealed partial class SalesPostingSqlTests
{
    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task SupplierPaymentHistoryIncludesDistributionLinesPagesWithoutDuplicatesAndResolvesBothJournals()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true); var s = fixture.Scenario;
            var setup = await VoucherSetupAsync(fixture).ConfigureAwait(true);
            using var db = database.Context(); var treasury = Treasury(db);
            var command = new SaveVoucherCommand(VoucherKind.Payment, setup.Payment, null, SettlementMethod.Cash,
                new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero), "USD", 1m, 10m, "دفع دون اسم في الرأس", [],
                [new(setup.Supplier, null, 4m), new(setup.Supplier, null, 6m)]);
            var ids = new HashSet<Guid>();
            VoucherDetailResult? first = null;
            for (var i = 0; i < 51; i++)
            {
                var row = await treasury.CreateDraftAsync(s.CompanyA, s.BranchA1, s.Actor, command, Cancellation).ConfigureAwait(true);
                first ??= row; ids.Add(row.Id);
            }
            var page1 = await treasury.ListAsync(s.CompanyA, s.BranchA1,
                new(Kind: VoucherKind.Payment, PartyId: setup.Supplier, PageSize: 50), Cancellation).ConfigureAwait(true);
            var page2 = await treasury.ListAsync(s.CompanyA, s.BranchA1,
                new(Kind: VoucherKind.Payment, PartyId: setup.Supplier, Page: 2, PageSize: 50), Cancellation).ConfigureAwait(true);
            Assert.Equal(51, page1.TotalCount); Assert.Equal(50, page1.Items.Count); Assert.Single(page2.Items);
            // The old header-only predicate missed every one of these supplier payments.
            Assert.Equal(0, await db.Vouchers.CountAsync(v => v.PartyId == setup.Supplier, Cancellation).ConfigureAwait(true));
            Assert.True(ids.SetEquals(page1.Items.Concat(page2.Items).Select(v => v.Id)));
            Assert.Empty((await treasury.ListAsync(s.CompanyA, s.BranchA2, new(PartyId: setup.Supplier), Cancellation).ConfigureAwait(true)).Items);
            Assert.Empty((await treasury.ListAsync(s.CompanyB, s.BranchB1, new(PartyId: setup.Supplier), Cancellation).ConfigureAwait(true)).Items);
            Assert.Empty((await treasury.ListAsync(s.CompanyA, s.BranchA1, new(PartyId: setup.Customer), Cancellation).ConfigureAwait(true)).Items);
            var posted = await treasury.PostAsync(s.CompanyA, s.BranchA1, first!.Id, s.Actor,
                new(ConcurrencyToken.FromBytes(first.RowVersion), "supplier-history-post"), Cancellation).ConfigureAwait(true);
            var byOriginal = await treasury.ListAsync(s.CompanyA, s.BranchA1, new(JournalEntryId: posted.JournalEntryId), Cancellation).ConfigureAwait(true);
            Assert.Equal(posted.Id, Assert.Single(byOriginal.Items).Id);
            var reversed = await treasury.ReverseAsync(s.CompanyA, s.BranchA1, posted.Id, s.Actor,
                new(ConcurrencyToken.FromBytes(posted.RowVersion), "تصحيح الدفع"), Cancellation).ConfigureAwait(true);
            Assert.Equal(posted.Id, Assert.Single((await treasury.ListAsync(s.CompanyA, s.BranchA1,
                new(JournalEntryId: reversed.ReversalJournalEntryId), Cancellation).ConfigureAwait(true)).Items).Id);
            Assert.Empty((await treasury.ListAsync(s.CompanyA, s.BranchA2,
                new(JournalEntryId: posted.JournalEntryId), Cancellation).ConfigureAwait(true)).Items);
            Assert.Empty((await treasury.ListAsync(s.CompanyA, s.BranchA1, new(JournalEntryId: Guid.NewGuid()), Cancellation).ConfigureAwait(true)).Items);
        }
    }
}
