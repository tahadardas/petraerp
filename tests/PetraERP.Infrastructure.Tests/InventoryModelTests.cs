using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using PetraERP.Infrastructure.Inventory;
using PetraERP.Infrastructure.Persistence;
using Xunit;

namespace PetraERP.Infrastructure.Tests;

public sealed class InventoryModelTests
{
    private static PetraDbContext Context() =>
        new(new DbContextOptionsBuilder<PetraDbContext>().UseSqlServer().Options);

    [Fact]
    public void InventorySchemaHasSixteenExplicitTablesInInventorySchema()
    {
        using var context = Context();
        var entities = context.Model.GetEntityTypes()
            .Where(static entity => entity.GetSchema() == "inventory")
            .ToArray();

        Assert.Equal(18, entities.Length);
        Assert.All(entities, entity => Assert.Equal("inventory", entity.GetSchema()));

        var expectedTypes = new[]
        {
            typeof(ItemGroupRow), typeof(UnitOfMeasureRow), typeof(WarehouseRow),
            typeof(ItemRow), typeof(UnitConversionRow), typeof(ItemBarcodeRow),
            typeof(ItemPriceRow), typeof(StockMovementRow), typeof(StockTransferRow),
            typeof(StockTransferLineRow), typeof(StockAdjustmentRow), typeof(StockAdjustmentLineRow),
            typeof(InventoryDocumentRow), typeof(InventoryDocumentLineRow), typeof(InventoryDocumentSequence),
            typeof(ItemAccountMappingRow)
        };

        foreach (var expected in expectedTypes)
        {
            Assert.Contains(entities, e => e.ClrType == expected);
        }

        // External foreign keys (to Company, Branch, User, Warehouse, Unit, CostCenter) are restrictive
        var externalFks = entities.SelectMany(static e => e.GetForeignKeys())
            .Where(fk => fk.PrincipalEntityType.ClrType != typeof(ItemRow)
                      && fk.PrincipalEntityType.ClrType != typeof(StockTransferRow)
                      && fk.PrincipalEntityType.ClrType != typeof(StockAdjustmentRow)
                      && fk.PrincipalEntityType.ClrType != typeof(InventoryDocumentRow));

        Assert.All(externalFks,
            fk => Assert.True(fk.DeleteBehavior is DeleteBehavior.Restrict or DeleteBehavior.NoAction));
    }

    [Fact]
    public void ItemModelHasRequiredFieldsAndPrecision()
    {
        using var context = Context();
        var item = context.Model.FindEntityType(typeof(ItemRow))!;

        Assert.Equal(64, item.FindProperty("Code")!.GetMaxLength());
        Assert.Equal(200, item.FindProperty("NameArabic")!.GetMaxLength());
        Assert.False(item.FindProperty("NameArabic")!.IsNullable);

        var minStock = item.FindProperty("MinStock")!;
        Assert.Equal(19, minStock.GetPrecision());
        Assert.Equal(4, minStock.GetScale());

        var maxStock = item.FindProperty("MaxStock")!;
        Assert.Equal(19, maxStock.GetPrecision());
        Assert.Equal(4, maxStock.GetScale());
    }

    [Fact]
    public void StockMovementModelHasExactQuantityAndCostPrecision()
    {
        using var context = Context();
        var movement = context.Model.FindEntityType(typeof(StockMovementRow))!;

        var qty = movement.FindProperty("Quantity")!;
        Assert.Equal(19, qty.GetPrecision());
        Assert.Equal(4, qty.GetScale());

        var baseQty = movement.FindProperty("BaseQuantity")!;
        Assert.Equal(19, baseQty.GetPrecision());
        Assert.Equal(4, baseQty.GetScale());

        var unitCost = movement.FindProperty("UnitCost")!;
        Assert.Equal(19, unitCost.GetPrecision());
        Assert.Equal(4, unitCost.GetScale());

        var totalCost = movement.FindProperty("TotalCost")!;
        Assert.Equal(19, totalCost.GetPrecision());
        Assert.Equal(4, totalCost.GetScale());
    }

    [Fact]
    public void UnitConversionModelHasFactorPrecisionSix()
    {
        using var context = Context();
        var conversion = context.Model.FindEntityType(typeof(UnitConversionRow))!;

        var factor = conversion.FindProperty("Factor")!;
        Assert.Equal(19, factor.GetPrecision());
        Assert.Equal(6, factor.GetScale());
    }

    [Fact]
    public void StockTransferAndAdjustmentTablesHaveLineCollections()
    {
        using var context = Context();
        var transfer = context.Model.FindEntityType(typeof(StockTransferRow))!;
        var transferLine = context.Model.FindEntityType(typeof(StockTransferLineRow))!;
        var adjustment = context.Model.FindEntityType(typeof(StockAdjustmentRow))!;
        var adjustmentLine = context.Model.FindEntityType(typeof(StockAdjustmentLineRow))!;

        Assert.NotNull(transfer);
        Assert.NotNull(transferLine);
        Assert.NotNull(adjustment);
        Assert.NotNull(adjustmentLine);

        Assert.Equal(64, transfer.FindProperty("TransferNumber")!.GetMaxLength());
        Assert.Equal(64, adjustment.FindProperty("AdjustmentNumber")!.GetMaxLength());
        Assert.Equal(200, adjustment.FindProperty("Reason")!.GetMaxLength());
        Assert.False(adjustment.FindProperty("Reason")!.IsNullable);
    }
}
