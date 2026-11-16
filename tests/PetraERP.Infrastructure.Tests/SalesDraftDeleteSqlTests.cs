using PetraERP.Application.Common;
using PetraERP.Application.Sales;

namespace PetraERP.Infrastructure.Tests;

// Draft deletion: only a never-approved invoice, only with the version the caller read; posted invoices are reversed instead.
public sealed partial class SalesPostingSqlTests
{
    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task DraftSalesInvoiceCanBeDeletedWithItsVersionAndPostedOneCannot()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true);
            var s = fixture.Scenario;
            using var context = database.Context();
            var sales = Service(context);

            var draft = await sales.GetInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, Cancellation).ConfigureAwait(true);
            var stale = ConcurrencyToken.FromBytes([1, 2, 3, 4, 5, 6, 7, 8]);
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
                sales.DeleteDraftAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, stale, Cancellation)).ConfigureAwait(true);

            await sales.DeleteDraftAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, ConcurrencyToken.FromBytes(draft.RowVersion), Cancellation).ConfigureAwait(true);
            await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
                sales.GetInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, Cancellation)).ConfigureAwait(true);

            var second = await sales.CreateInvoiceAsync(s.CompanyA, s.BranchA1, s.Actor,
                new CreateSalesInvoiceCommand(draft.PatternRevisionId, draft.CustomerId, draft.WarehouseId,
                    new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero), PetraERP.Domain.Sales.PaymentMethod.Credit, CurrencyCode: "USD", ExchangeRate: 1m,
                    Lines: [new(fixture.ItemId, fixture.UnitId, 1m, 100m)]), Cancellation).ConfigureAwait(true);
            await sales.PostInvoiceAsync(s.CompanyA, s.BranchA1, second.Id, s.Actor, new PostSalesInvoiceCommand("del-posted"), Cancellation).ConfigureAwait(true);
            var posted = await sales.GetInvoiceAsync(s.CompanyA, s.BranchA1, second.Id, Cancellation).ConfigureAwait(true);
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                sales.DeleteDraftAsync(s.CompanyA, s.BranchA1, second.Id, ConcurrencyToken.FromBytes(posted.RowVersion), Cancellation)).ConfigureAwait(true);
            Assert.Equal("sales.invoice_not_draft", error.Code);
        }
    }
}
