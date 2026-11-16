#pragma warning disable CA1707

using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Inventory.Opening;
using PetraERP.Application.Security;
using PetraERP.Infrastructure.Inventory;
using PetraERP.Infrastructure.Modules;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Persistence.Actors;
using PetraERP.Modules.Abstractions;

namespace PetraERP.Infrastructure.Tests;

/// <summary>
/// Stock moved by a module, on the production context path (a fresh annual context bound to one write operation per step):
/// the balances change exactly once, a replay returns the original movement, and the engine's stock guard still applies.
/// Requires PETRAERP_SQL_TEST_SERVER.
/// </summary>
public sealed class ModuleInventoryMovementGatewaySqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ModuleTransferMovesStockOnceReplaysSafelyAndRespectsTheStockGuard()
    {
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            var provisioned = await fixture.ProvisionAsync().ConfigureAwait(true);
            Assert.True(provisioned.Succeeded, provisioned.Error);

            Guid companyId;
            Guid branchId;
            Guid warehouseA;
            DateOnly date;
            var warehouseB = Guid.NewGuid();
            var unitId = Guid.NewGuid();
            var itemId = Guid.NewGuid();
            using (var read = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                var company = await read.Companies.SingleAsync(Cancellation).ConfigureAwait(true);
                companyId = company.Id;
                date = DateOnly.FromDateTime((await read.FiscalYears.SingleAsync(y => y.Id == provisioned.FiscalYearId, Cancellation).ConfigureAwait(true)).RangeStart.ToDateTime(TimeOnly.MinValue).AddDays(10));
                branchId = (await read.Branches.SingleAsync(Cancellation).ConfigureAwait(true)).Id;
                warehouseA = (await read.Warehouses.SingleAsync(w => w.CompanyId == company.Id, Cancellation).ConfigureAwait(true)).Id;
            }

            using (var master = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                master.BindWriteScope(new(companyId, provisioned.FiscalYearId, fixture.Actor, AccountingOperation.ManageItems, branchId));
                master.UnitsOfMeasure.Add(new UnitOfMeasureRow { Id = unitId, CompanyId = companyId, Code = "PCS", NameArabic = "Piece", Symbol = "pcs", IsActive = true });
                master.Items.Add(new ItemRow { Id = itemId, CompanyId = companyId, Code = "ITEM-001", NameArabic = "Item", NormalizedSearchText = "item", BaseUnitId = unitId, IsActive = true, CostMethod = 1 });
                await master.SaveChangesAsync(Cancellation).ConfigureAwait(true);
            }

            using (var warehouses = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                warehouses.BindWriteScope(new(companyId, provisioned.FiscalYearId, fixture.Actor, AccountingOperation.ManageWarehouses, branchId));
                warehouses.Warehouses.Add(new WarehouseRow { Id = warehouseB, CompanyId = companyId, BranchId = branchId, Code = "WH-B", NameArabic = "Second warehouse", IsActive = true });
                await warehouses.SaveChangesAsync(Cancellation).ConfigureAwait(true);
            }

            using (var stock = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                stock.BindWriteScope(new(companyId, provisioned.FiscalYearId, fixture.Actor, AccountingOperation.ManageTransfers, branchId));
                await new SqlInventoryOpeningService(stock, TimeProvider.System).PostOpeningStockBatchAsync(companyId, fixture.Actor,
                    new CreateOpeningStockBatchCommand(branchId, new DateTimeOffset(date.AddDays(-5).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), "OPEN-1", "OPEN-IDEMP-1",
                        [new OpeningStockLineInput(warehouseA, itemId, unitId, 100m, 1000m, "Opening stock")]), Cancellation).ConfigureAwait(true);
            }

            var gateway = new SqlInventoryMovementGateway(new AccountingModuleLedgerContexts(fixture.Factory), TimeProvider.System);
            var context = new ModuleRequestContext(companyId, provisioned.FiscalYearId, branchId, fixture.Actor, "ar",
                new HashSet<string> { PermissionCatalog.InventoryTransfersManage }, "demo.stock");

            var key = Guid.NewGuid();
            var request = new InventoryMovementRequest(itemId, warehouseA, warehouseB, unitId, 30m, date, "restock branch B", key);
            var moved = await gateway.MoveAsync(context, request, Cancellation).ConfigureAwait(true);
            Assert.False(moved.WasAlreadyRecorded);

            var a = await gateway.QueryBalanceAsync(context, itemId, warehouseA, Cancellation).ConfigureAwait(true);
            var b = await gateway.QueryBalanceAsync(context, itemId, warehouseB, Cancellation).ConfigureAwait(true);
            Assert.Equal(70m, Assert.Single(a.Lines).Quantity);
            Assert.Equal(30m, Assert.Single(b.Lines).Quantity);
            Assert.Equal(300m, Assert.Single(b.Lines).Value);   // cost travels with the stock (WAC 10 per unit)

            var replay = await gateway.MoveAsync(context, request, Cancellation).ConfigureAwait(true);
            Assert.True(replay.WasAlreadyRecorded);
            Assert.Equal(moved.MovementId, replay.MovementId);
            Assert.Equal(70m, Assert.Single((await gateway.QueryBalanceAsync(context, itemId, warehouseA, Cancellation).ConfigureAwait(true)).Lines).Quantity);

            // The core's stock guard still applies: a module cannot move more than exists.
            await Assert.ThrowsAnyAsync<Exception>(() => gateway.MoveAsync(context,
                request with { Quantity = 500m, IdempotencyKey = Guid.NewGuid() }, Cancellation)).ConfigureAwait(true);
            Assert.Equal(70m, Assert.Single((await gateway.QueryBalanceAsync(context, itemId, warehouseA, Cancellation).ConfigureAwait(true)).Lines).Quantity);

            // The API path: a context with NO operation bound beforehand (as the inventory endpoints create it). Reading and saving
            // a transfer used to fail with "write_scope_required"; the service now binds the operation that matches the kind.
            var apiScope = new PetraERP.Application.Inventory.InventoryDocumentScope(companyId, branchId, fixture.Actor);
            using (var unbound = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                var service = new SqlInventoryDocumentService(unbound, TimeProvider.System);
                var page = await service.ListAsync(apiScope, null, 1, 50, Cancellation).ConfigureAwait(true);
                // The posted transfer plus the draft of the over-sized move the stock guard refused (kept, resumable with its key).
                Assert.Equal(2, page.TotalCount);
                Assert.Equal(2, (await service.BalancesAsync(apiScope, null, itemId, Cancellation).ConfigureAwait(true)).Count);
            }

            using (var unbound = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                var saved = await new SqlInventoryDocumentService(unbound, TimeProvider.System).SaveAsync(apiScope, null,
                    new PetraERP.Application.Inventory.SaveInventoryDocumentCommand(PetraERP.Domain.Inventory.InventoryDocumentKind.Transfer, warehouseA, warehouseB,
                        new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), "api style draft", null, null, null,
                        [new PetraERP.Application.Inventory.InventoryDocumentLineInput(itemId, unitId, 5m)], null), Cancellation).ConfigureAwait(true);
                Assert.Equal(PetraERP.Domain.Inventory.InventoryDocumentState.Draft, saved.State);
            }
        }
    }
}
