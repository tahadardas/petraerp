using PetraERP.Domain.Inventory;
using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.Tests;

public sealed class InventoryDomainTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid BranchId = Guid.NewGuid();
    private static readonly Guid WarehouseAId = Guid.NewGuid();
    private static readonly Guid WarehouseBId = Guid.NewGuid();
    private static readonly Guid KgUnitId = Guid.NewGuid();
    private static readonly Guid TonUnitId = Guid.NewGuid();
    private static readonly Guid ItemId = Guid.NewGuid();
    private static readonly Guid CostCenterId = Guid.NewGuid();

    [Fact]
    public void UnitConversionValidatesFactorAndDistinctUnits()
    {
        var id = Guid.NewGuid();
        // Disallows same unit
        var exSame = Assert.Throws<InvalidOperationException>(() =>
            new UnitConversion(id, CompanyId, KgUnitId, KgUnitId, 1000m));
        Assert.Equal(RuleViolation.UnitConversionInvalid, exSame.Data[RuleViolation.DataKey]);

        // Disallows zero or negative factor
        var exZero = Assert.Throws<InvalidOperationException>(() =>
            new UnitConversion(id, CompanyId, TonUnitId, KgUnitId, 0m));
        Assert.Equal(RuleViolation.UnitConversionInvalid, exZero.Data[RuleViolation.DataKey]);

        // Successful conversion
        var conversion = new UnitConversion(id, CompanyId, TonUnitId, KgUnitId, 1000m);
        Assert.Equal(1000m, conversion.Factor);
        Assert.Equal(TonUnitId, conversion.FromUnitId);
        Assert.Equal(KgUnitId, conversion.ToUnitId);
    }

    [Fact]
    public void StockTransferDisallowsSameSourceAndTargetWarehouse()
    {
        var id = Guid.NewGuid();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new StockTransfer(id, CompanyId, "TR-001", BranchId, WarehouseAId, BranchId, WarehouseAId, DateTimeOffset.UtcNow));
        Assert.Equal(RuleViolation.WarehouseTransferInvalid, ex.Data[RuleViolation.DataKey]);
    }

    [Fact]
    public void StockTransferAllowsDistinctWarehousesAndManagesLifecycle()
    {
        var id = Guid.NewGuid();
        var transfer = new StockTransfer(id, CompanyId, "TR-001", BranchId, WarehouseAId, BranchId, WarehouseBId, DateTimeOffset.UtcNow);
        Assert.Equal(StockTransferStatus.Draft, transfer.Status);

        transfer.AddLine(new StockTransferLine(Guid.NewGuid(), id, ItemId, KgUnitId, 50m, 50m, 2.5m, 125m));
        Assert.Single(transfer.Lines);

        transfer.Complete();
        Assert.Equal(StockTransferStatus.Completed, transfer.Status);

        Assert.Throws<InvalidOperationException>(() => transfer.Cancel());
    }

    [Fact]
    public void StockMovementInternalIssueStrictlyRequiresCostCenter()
    {
        var id = Guid.NewGuid();
        // Missing cost center throws CostCenterRequiredForIssue (PT-091)
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new StockMovement(
                id, CompanyId, BranchId, WarehouseAId, ItemId, KgUnitId,
                StockMovementKind.InternalIssue,
                quantity: 20m, baseQuantity: -20m, unitCost: 1.5m, totalCost: 30m,
                movementDateUtc: DateTimeOffset.UtcNow,
                documentType: "IssueVoucher",
                costCenterId: null));
        Assert.Equal(RuleViolation.CostCenterRequiredForIssue, ex.Data[RuleViolation.DataKey]);

        // With valid cost center succeeds
        var movement = new StockMovement(
            id, CompanyId, BranchId, WarehouseAId, ItemId, KgUnitId,
            StockMovementKind.InternalIssue,
            quantity: 20m, baseQuantity: -20m, unitCost: 1.5m, totalCost: 30m,
            movementDateUtc: DateTimeOffset.UtcNow,
            documentType: "IssueVoucher",
            costCenterId: CostCenterId);

        Assert.Equal(CostCenterId, movement.CostCenterId);
        Assert.Equal(StockMovementKind.InternalIssue, movement.MovementKind);
        Assert.True(movement.MovementKind.IsOutward());
        Assert.False(movement.MovementKind.IsInward());
        Assert.Equal(-1, movement.MovementKind.DirectionSign());
    }

    [Fact]
    public void StockMovementKindDirectionsAreConsistent()
    {
        Assert.True(StockMovementKind.OpeningBalance.IsInward());
        Assert.True(StockMovementKind.PurchaseReceipt.IsInward());
        Assert.True(StockMovementKind.TransferIn.IsInward());
        Assert.True(StockMovementKind.AdjustmentIncrease.IsInward());
        Assert.True(StockMovementKind.SalesReturn.IsInward());

        Assert.True(StockMovementKind.PurchaseReturn.IsOutward());
        Assert.True(StockMovementKind.SalesDelivery.IsOutward());
        Assert.True(StockMovementKind.TransferOut.IsOutward());
        Assert.True(StockMovementKind.AdjustmentDecrease.IsOutward());
        Assert.True(StockMovementKind.InternalIssue.IsOutward());
    }

    [Fact]
    public void ItemStockLevelsValidateMinMaxRelationship()
    {
        var code = EntityCode.Create("ITM-01");
        var name = BilingualName.Create("مادة تجريبية", "Test Item");

        // Min > Max throws
        Assert.Throws<ArgumentException>(() =>
            new Item(ItemId, CompanyId, code, name, KgUnitId, CostMethod.WeightedAverage, minStock: 100m, maxStock: 50m));

        // Negative stock level throws
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Item(ItemId, CompanyId, code, name, KgUnitId, CostMethod.WeightedAverage, minStock: -5m));

        // Valid stock levels succeed
        var item = new Item(ItemId, CompanyId, code, name, KgUnitId, CostMethod.Fifo, minStock: 10m, maxStock: 100m, reorderPoint: 25m);
        Assert.Equal(CostMethod.Fifo, item.CostMethod);
        Assert.Equal(10m, item.MinStock);
        Assert.Equal(100m, item.MaxStock);
        Assert.Equal(25m, item.ReorderPoint);
    }

    [Fact]
    public void StockAdjustmentManagesReasonAndApproval()
    {
        var id = Guid.NewGuid();
        // Missing reason throws
        Assert.Throws<ArgumentException>(() =>
            new StockAdjustment(id, CompanyId, BranchId, WarehouseAId, "ADJ-001", DateTimeOffset.UtcNow, "   "));

        var adjustment = new StockAdjustment(id, CompanyId, BranchId, WarehouseAId, "ADJ-001", DateTimeOffset.UtcNow, "Annual physical inventory count");
        adjustment.AddLine(new StockAdjustmentLine(Guid.NewGuid(), id, ItemId, KgUnitId, systemQuantity: 100m, countedQuantity: 95m, unitCost: 10m));

        Assert.Equal(-5m, adjustment.Lines[0].DifferenceQuantity);
        Assert.Equal(StockAdjustmentStatus.Draft, adjustment.Status);

        var approverId = Guid.NewGuid();
        adjustment.Approve(approverId);
        Assert.Equal(StockAdjustmentStatus.Approved, adjustment.Status);
        Assert.Equal(approverId, adjustment.ApprovedByUserId);
        Assert.NotNull(adjustment.ApprovedAtUtc);
    }
}
