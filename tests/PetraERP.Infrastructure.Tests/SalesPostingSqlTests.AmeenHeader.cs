using PetraERP.Application.Common;
using PetraERP.Application.Sales;

namespace PetraERP.Infrastructure.Tests;

public sealed partial class SalesPostingSqlTests
{
    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task AmeenHeaderSellerAndDistributorCodesPersistOnTheDraftAndSurviveReload()
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

            Assert.Null(current.SellerCode);
            Assert.Null(current.DistributorCode);
            using (var writer = database.Context())
            {
                await Service(writer).UpdateInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId,
                    new UpdateSalesInvoiceCommand(current.CustomerId, current.WarehouseId, current.InvoiceDateUtc, CurrencyCode: "USD",
                        Lines: [new(fixture.ItemId, fixture.UnitId, 1, 10)])
                    { ExpectedVersion = ConcurrencyToken.FromBytes(current.RowVersion), SellerCode = 1, DistributorCode = 0 }, Cancellation).ConfigureAwait(true);
            }

            using var verify = database.Context();
            var stored = await Service(verify).GetInvoiceAsync(s.CompanyA, s.BranchA1, fixture.InvoiceId, Cancellation).ConfigureAwait(true);
            Assert.Equal(1, stored.SellerCode);
            Assert.Equal(0, stored.DistributorCode);
        }
    }
}
