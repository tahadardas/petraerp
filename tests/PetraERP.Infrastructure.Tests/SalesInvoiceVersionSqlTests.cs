using PetraERP.Application.Common;
using PetraERP.Application.Sales;
using PetraERP.Domain.Sales;

namespace PetraERP.Infrastructure.Tests;

public sealed partial class SalesPostingSqlTests
{
    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ConcurrentDraftWritersWithOneVersionLetExactlyOneWinAndKeepItsChanges()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var fixture = await CreateAsync(database).ConfigureAwait(true); var s = fixture.Scenario;
            SalesInvoiceDetailResult current;
            using (var reader = database.Context())
            {
                current = await Service(reader).GetInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, Cancellation).ConfigureAwait(true);
            }

            var version = ConcurrencyToken.FromBytes(current.RowVersion);
            async Task<string?> WriteAsync(string notes)
            {
                using var context = database.Context();
                try
                {
                    await Service(context).UpdateInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId,
                        new UpdateSalesInvoiceCommand(current.CustomerId, current.WarehouseId, current.InvoiceDateUtc, CurrencyCode: "USD", Notes: notes,
                            Lines: [new(fixture.ItemId, fixture.UnitId, 1, 10)]) { ExpectedVersion = version }, Cancellation).ConfigureAwait(false);
                    return notes;
                }
                catch (ConcurrencyConflictException)
                {
                    return null;
                }
            }

            async Task<bool> ApproveAsync()
            {
                using var context = database.Context();
                try
                {
                    await Service(context).ApproveInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, s.Actor, version, Cancellation).ConfigureAwait(false);
                    return true;
                }
                catch (ConcurrencyConflictException)
                {
                    return false;
                }
            }

            var writers = await Task.WhenAll(WriteAsync("كاتب أول"), WriteAsync("كاتب ثان"), WriteAsync("كاتب ثالث")).ConfigureAwait(true);
            var winner = Assert.Single(writers, w => w is not null);
            // Approval with the version read before the winning write is rejected as well.
            Assert.False(await ApproveAsync().ConfigureAwait(true));

            using var verify = database.Context();
            var stored = await Service(verify).GetInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, Cancellation).ConfigureAwait(true);
            Assert.Equal(winner, stored.Notes);
            Assert.Equal(SalesInvoiceStatus.Draft, stored.Status);
            Assert.Single(stored.Lines);
        }
    }
}
