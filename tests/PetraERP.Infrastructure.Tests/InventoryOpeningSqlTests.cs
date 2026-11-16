#pragma warning disable CA1707

using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Inventory.Opening;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Inventory;
using PetraERP.Infrastructure.Inventory;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Persistence.Actors;

namespace PetraERP.Infrastructure.Tests;

public sealed class InventoryOpeningSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task InventoryOpening_PostsStockMovements_CalculatesInitialWac()
    {
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            var provisioned = await fixture.ProvisionAsync().ConfigureAwait(true);
            Assert.True(provisioned.Succeeded, provisioned.Error);

            Guid companyId;
            Guid branchId;
            Guid warehouseId;
            var uomId = Guid.NewGuid();
            var itemId = Guid.NewGuid();

            using (var readContext = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                var company = await readContext.Companies.SingleAsync(Cancellation).ConfigureAwait(true);
                var branch = await readContext.Branches.SingleAsync(Cancellation).ConfigureAwait(true);
                var warehouse = await readContext.Warehouses.SingleAsync(w => w.CompanyId == company.Id, Cancellation).ConfigureAwait(true);
                companyId = company.Id;
                branchId = branch.Id;
                warehouseId = warehouse.Id;
            }

            // 1. Create Unit of Measure & Item
            using (var itemContext = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                itemContext.BindWriteScope(new(companyId, provisioned.FiscalYearId, fixture.Actor, AccountingOperation.ManageItems, branchId));
                var uom = new UnitOfMeasureRow
                {
                    Id = uomId,
                    CompanyId = companyId,
                    Code = "PCS",
                    NameArabic = "قطعة",
                    Symbol = "pcs",
                    IsActive = true
                };
                var item = new ItemRow
                {
                    Id = itemId,
                    CompanyId = companyId,
                    Code = "ITEM-001",
                    NameArabic = "صنف تجريبي",
                    NormalizedSearchText = "صنف تجريبي",
                    BaseUnitId = uomId,
                    IsActive = true,
                    CostMethod = 1
                };
                itemContext.UnitsOfMeasure.Add(uom);
                itemContext.Items.Add(item);
                await itemContext.SaveChangesAsync(Cancellation).ConfigureAwait(true);
            }

            // 2. Post opening inventory: 100 units @ $10.50 each = $1,050.00
            OpeningStockBatchDto result;
            using (var stockContext = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                stockContext.BindWriteScope(new(companyId, provisioned.FiscalYearId, fixture.Actor, AccountingOperation.ManageTransfers, branchId));
                var service = new SqlInventoryOpeningService(stockContext, TimeProvider.System);
                var command = new CreateOpeningStockBatchCommand(
                    BranchId: branchId,
                    OpeningDateUtc: DateTimeOffset.UtcNow,
                    SourceReference: "OPEN-STOCK-2026",
                    IdempotencyKey: "OPEN-STOCK-IDEMP-01",
                    Lines: [
                        new OpeningStockLineInput(warehouseId, itemId, uomId, 100m, 1050m, "Opening stock")
                    ]);

                result = await service.PostOpeningStockBatchAsync(companyId, fixture.Actor, command, Cancellation).ConfigureAwait(true);
            }

            Assert.NotNull(result);
            Assert.Single(result.Lines);
            Assert.Equal(100m, result.TotalQuantity);
            Assert.Equal(1050m, result.TotalValue);

            // 3. Verify stock movement was recorded in database
            using (var readContext = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                var movement = await readContext.StockMovements.SingleAsync(m => m.ItemId == itemId, Cancellation).ConfigureAwait(true);
                Assert.Equal(100m, movement.Quantity);
                Assert.Equal(10.50m, movement.UnitCost);
                Assert.Equal(100m, movement.QuantityAfter);
                Assert.Equal(1050m, movement.ValueAfter);
                Assert.Equal(warehouseId, movement.WarehouseId);
                Assert.Equal("OpeningBalance", movement.DocumentType);
            }
        }
    }
}
