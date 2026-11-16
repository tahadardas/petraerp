using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Sales;
using PetraERP.Infrastructure.Sales;

namespace PetraERP.Infrastructure.Tests;

public sealed partial class SalesPostingSqlTests
{
    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task SalesLinksResolveBothDirectionsIncludingReversalsWithoutCrossingScope()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var f = await CreateAsync(database).ConfigureAwait(true); var s = f.Scenario;
            using var db = database.Context();
            var posted = await Service(db).PostInvoiceAsync(s.CompanyA, s.BranchA1, f.InvoiceId, s.Actor,
                new("source-links-post", null), Cancellation).ConfigureAwait(true);
            var reversed = await Service(db).ReverseInvoiceAsync(s.CompanyA, s.BranchA1, f.InvoiceId, s.Actor,
                new("تصحيح فاتورة مرتبطة"), Cancellation).ConfigureAwait(true);
            var links = new SqlSalesDocumentLinkService(db);
            var customer = await links.GetAsync(s.CompanyA, s.BranchA1, SalesLinkSource.Customer, posted.CustomerId, 1, 1, Cancellation).ConfigureAwait(true);
            var second = await links.GetAsync(s.CompanyA, s.BranchA1, SalesLinkSource.Customer, posted.CustomerId, 2, 1, Cancellation).ConfigureAwait(true);
            Assert.Equal(2, customer.TotalCount); Assert.Single(customer.Items); Assert.Single(second.Items);
            Assert.NotEqual(customer.Items[0].Id, second.Items[0].Id);
            var item = await links.GetAsync(s.CompanyA, s.BranchA1, SalesLinkSource.Item, f.ItemId, 1, 50, Cancellation).ConfigureAwait(true);
            Assert.Equal(2, item.TotalCount); Assert.All(item.Items, row => Assert.Equal("عميل الاختبار", row.Description));
            Assert.All(item.Items, row => Assert.Equal(210m, row.Amount));
            foreach (var invoiceId in new[] { f.InvoiceId, reversed.ReversedByInvoiceId!.Value })
            {
                var invoice = await db.SalesInvoices.SingleAsync(i => i.Id == invoiceId, Cancellation).ConfigureAwait(true);
                var journal = await links.GetAsync(s.CompanyA, s.BranchA1, SalesLinkSource.Journal, invoice.JournalEntryId!.Value, 1, 50, Cancellation).ConfigureAwait(true);
                Assert.Equal(invoiceId, Assert.Single(journal.Items).Id);
                var movements = await links.GetAsync(s.CompanyA, s.BranchA1, SalesLinkSource.Invoice, invoiceId, 1, 50, Cancellation).ConfigureAwait(true);
                var movement = Assert.Single(movements.Items); Assert.Equal(f.ItemId, movement.ItemId);
                Assert.Equal(invoiceId == f.InvoiceId ? -2m : 2m, movement.Quantity); Assert.Equal(4m, movement.Amount); Assert.Equal("USD", movement.CurrencyCode);
                var origin = await links.GetAsync(s.CompanyA, s.BranchA1, SalesLinkSource.Movement, movement.Id, 1, 50, Cancellation).ConfigureAwait(true);
                Assert.Equal(invoiceId, Assert.Single(origin.Items).Id);
                await Assert.ThrowsAsync<ResourceNotFoundException>(() => links.GetAsync(s.CompanyA, s.BranchA2, SalesLinkSource.Movement, movement.Id, 1, 50, Cancellation)).ConfigureAwait(true);
                await Assert.ThrowsAsync<ResourceNotFoundException>(() => links.GetAsync(s.CompanyB, s.BranchB1, SalesLinkSource.Invoice, invoiceId, 1, 50, Cancellation)).ConfigureAwait(true);
            }
            Assert.Empty((await links.GetAsync(s.CompanyA, s.BranchA2, SalesLinkSource.Customer, posted.CustomerId, 1, 50, Cancellation).ConfigureAwait(true)).Items);
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => links.GetAsync(s.CompanyB, s.BranchB1, SalesLinkSource.Customer, posted.CustomerId, 1, 50, Cancellation)).ConfigureAwait(true);
            // A non-sales movement carrying a sales document id is never interpreted as a backlink.
            var receipt = await db.StockMovements.FirstAsync(m => m.DocumentType == "OpeningBalance", Cancellation).ConfigureAwait(true);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [inventory].[StockMovements] SET [DocumentId]={f.InvoiceId} WHERE [Id]={receipt.Id}", Cancellation).ConfigureAwait(true);
            Assert.Empty((await links.GetAsync(s.CompanyA, s.BranchA1, SalesLinkSource.Movement, receipt.Id, 1, 50, Cancellation).ConfigureAwait(true)).Items);
        }
    }

    [Theory(Explicit = true)]
    [InlineData(0, 50)] [InlineData(1, 0)] [InlineData(1, 201)] [InlineData(int.MaxValue, 200)]
    [Trait("Category", "SqlServer")]
    public async Task SalesLinkPagingRejectsInvalidBoundsBeforeQuerying(int page, int size)
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var context = database.Context();
            await Assert.ThrowsAsync<RequestValidationException>(() => new SqlSalesDocumentLinkService(context)
                .GetAsync(Guid.NewGuid(), Guid.NewGuid(), SalesLinkSource.Item, Guid.NewGuid(), page, size, Cancellation)).ConfigureAwait(true);
        }
    }
}
