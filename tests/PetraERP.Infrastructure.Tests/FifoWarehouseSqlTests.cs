using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Inventory;
using PetraERP.Domain.Inventory;
using PetraERP.Infrastructure.Inventory;

namespace PetraERP.Infrastructure.Tests;

public sealed class FifoWarehouseSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static StockMovementRow Movement(LedgerScenario s, Guid warehouse, Guid item, Guid unit, StockMovementKind kind, decimal quantity, decimal totalCost) => new()
    {
        Id = Guid.NewGuid(), CompanyId = s.CompanyA, BranchId = s.BranchA1, WarehouseId = warehouse, ItemId = item, UnitId = unit, MovementKind = (int)kind,
        Quantity = Math.Abs(quantity), BaseQuantity = quantity, UnitCost = Math.Round(totalCost / Math.Abs(quantity), 4), TotalCost = totalCost, MovementDateUtc = DateTimeOffset.UtcNow,
        DocumentType = "Test", DocumentId = Guid.NewGuid(), CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = s.Actor,
    };

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task FifoGoldenDatasetIssueOf120Costs1300AndLeaves80Worth1200()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var inventory = new SqlInventoryService(context);
            var unit = await inventory.CreateUnitAsync(scenario.CompanyA, new CreateUnitCommand("GOLD", "قطعة", null, "pcs", 0), Cancellation).ConfigureAwait(true);
            var warehouse = await inventory.CreateWarehouseAsync(scenario.CompanyA, new CreateWarehouseCommand(scenario.BranchA1, "GOLD", "مستودع اختبار", null, null), Cancellation).ConfigureAwait(true);
            var item = await inventory.CreateItemAsync(scenario.CompanyA, new CreateItemCommand("GOLD", "مادة اختبار", null, null, unit.Id, warehouse.Id, CostMethod.Fifo, null, null, null), Cancellation).ConfigureAwait(true);

            await LedgerScenario.AddStockMovementAsync(context, Movement(scenario, warehouse.Id, item.Id, unit.Id, StockMovementKind.PurchaseReceipt, 100m, 1000m), Cancellation).ConfigureAwait(true);
            await LedgerScenario.AddStockMovementAsync(context, Movement(scenario, warehouse.Id, item.Id, unit.Id, StockMovementKind.PurchaseReceipt, 100m, 1500m), Cancellation).ConfigureAwait(true);
            var issue = Movement(scenario, warehouse.Id, item.Id, unit.Id, StockMovementKind.SalesDelivery, -120m, 1300m);
            await LedgerScenario.AddStockMovementAsync(context, issue, Cancellation).ConfigureAwait(true);

            var layers = await context.Set<InventoryCostLayerRow>().AsNoTracking().Where(l => l.ItemId == item.Id)
                .OrderBy(l => l.SourceSequence).ToListAsync(Cancellation).ConfigureAwait(true);
            var allocations = await context.Set<InventoryCostAllocationRow>().AsNoTracking().Where(a => a.MovementId == issue.Id)
                .ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal([0m, 80m], layers.Select(l => l.QuantityRemaining));
            Assert.Equal([0m, 1200m], layers.Select(l => l.ValueRemaining));
            Assert.Equal(120m, allocations.Sum(a => a.Quantity));
            Assert.Equal(1300m, allocations.Sum(a => a.Cost));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task FifoIssueOfOneHundredFiftyFromHundredAt100AndHundredAt120CostsSixteenThousandAndKeepsTheRemainingLayer()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var s = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var service = new SqlInventoryService(context);
            var unit = await service.CreateUnitAsync(s.CompanyA, new CreateUnitCommand("PCS", "قطعة", null, "pcs", 0), Cancellation).ConfigureAwait(true);
            var wh = await service.CreateWarehouseAsync(s.CompanyA, new CreateWarehouseCommand(s.BranchA1, "WH-F", "مستودع FIFO", null, null), Cancellation).ConfigureAwait(true);
            var item = await service.CreateItemAsync(s.CompanyA, new CreateItemCommand("FIFO-1", "مادة FIFO", null, null, unit.Id, wh.Id, CostMethod.Fifo, null, null, null), Cancellation).ConfigureAwait(true);

            await LedgerScenario.AddStockMovementAsync(context, Movement(s, wh.Id, item.Id, unit.Id, StockMovementKind.PurchaseReceipt, 100m, 10000m), Cancellation).ConfigureAwait(true);
            await LedgerScenario.AddStockMovementAsync(context, Movement(s, wh.Id, item.Id, unit.Id, StockMovementKind.PurchaseReceipt, 100m, 12000m), Cancellation).ConfigureAwait(true);

            // A wrong issue cost is refused: the FIFO allocation alone decides the cost.
            var wrong = await Assert.ThrowsAsync<BusinessRuleException>(() => LedgerScenario.AddStockMovementAsync(context, Movement(s, wh.Id, item.Id, unit.Id, StockMovementKind.SalesDelivery, -150m, 15000m), Cancellation)).ConfigureAwait(true);
            Assert.Equal("inventory.fifo_cost_mismatch", wrong.Code);
            context.ChangeTracker.Clear();

            await LedgerScenario.AddStockMovementAsync(context, Movement(s, wh.Id, item.Id, unit.Id, StockMovementKind.SalesDelivery, -150m, 16000m), Cancellation).ConfigureAwait(true);
            var layers = await context.Set<InventoryCostLayerRow>().AsNoTracking().Where(l => l.ItemId == item.Id).OrderBy(l => l.SourceSequence).ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal([0m, 50m], layers.Select(l => l.QuantityRemaining));
            Assert.Equal([0m, 6000m], layers.Select(l => l.ValueRemaining));
            var allocations = await context.Set<InventoryCostAllocationRow>().AsNoTracking().ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(16000m, allocations.Sum(a => a.Cost));
            Assert.Equal(150m, allocations.Sum(a => a.Quantity));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task WarehouseAccountingProfileValidatesParentCycleAndPostableAccounts()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var s = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var inventory = new SqlInventoryService(context);
            var accounting = new SqlWarehouseAccountingService(context);
            var a = await inventory.CreateWarehouseAsync(s.CompanyA, new CreateWarehouseCommand(s.BranchA1, "WH-A", "أ", null, null), Cancellation).ConfigureAwait(true);
            var b = await inventory.CreateWarehouseAsync(s.CompanyA, new CreateWarehouseCommand(s.BranchA1, "WH-B", "ب", null, null), Cancellation).ConfigureAwait(true);
            var stock = await s.AccountAsync(s.CompanyA, "1301", PetraERP.Application.GeneralLedger.ChartOfAccounts.AccountTypeCode.Assets, true).ConfigureAwait(true);
            var header = await s.AccountAsync(s.CompanyA, "13", PetraERP.Application.GeneralLedger.ChartOfAccounts.AccountTypeCode.Assets, false).ConfigureAwait(true);

            var saved = await accounting.SetAsync(s.CompanyA, s.BranchA1, b.Id, new SetWarehouseAccountingCommand(a.Id, stock.Id, null, null), Cancellation).ConfigureAwait(true);
            Assert.Equal(a.Id, saved.ParentWarehouseId);
            Assert.Equal("1301", saved.InventoryAccountCode);
            Assert.Equal(stock.Id, (await accounting.GetAsync(s.CompanyA, s.BranchA1, b.Id, Cancellation).ConfigureAwait(true)).InventoryAccountId);

            var cycle = await Assert.ThrowsAsync<BusinessRuleException>(() => accounting.SetAsync(s.CompanyA, s.BranchA1, a.Id, new SetWarehouseAccountingCommand(b.Id, null, null, null), Cancellation)).ConfigureAwait(true);
            Assert.Equal("warehouse.parent_cycle", cycle.Code);
            var self = await Assert.ThrowsAsync<BusinessRuleException>(() => accounting.SetAsync(s.CompanyA, s.BranchA1, a.Id, new SetWarehouseAccountingCommand(a.Id, null, null, null), Cancellation)).ConfigureAwait(true);
            Assert.Equal("warehouse.parent_self", self.Code);
            var header2 = await Assert.ThrowsAsync<BusinessRuleException>(() => accounting.SetAsync(s.CompanyA, s.BranchA1, a.Id, new SetWarehouseAccountingCommand(null, header.Id, null, null), Cancellation)).ConfigureAwait(true);
            Assert.Equal("warehouse.account_invalid", header2.Code);
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => accounting.GetAsync(s.CompanyB, s.BranchA1, a.Id, Cancellation)).ConfigureAwait(true);
        }
    }
}
