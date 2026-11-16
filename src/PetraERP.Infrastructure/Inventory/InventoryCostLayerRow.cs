using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Inventory;

/// <summary>
/// One receipt of a FIFO item in one warehouse. Issues consume layers oldest-first by posting order
/// (<see cref="SourceSequence"/> = the receipt movement's recorded sequence); a movement dated earlier than another
/// never overtakes it, so back-dating changes the document date, not the consumption order.
/// </summary>
internal sealed class InventoryCostLayerRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid ItemId { get; set; }
    public Guid ReceiptMovementId { get; set; }
    public long SourceSequence { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
    public decimal QuantityReceived { get; set; }
    public decimal QuantityRemaining { get; set; }
    public decimal ValueReceived { get; set; }
    public decimal ValueRemaining { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>Append-only ledger of layer consumption (positive) and restoration by a linked reversal (negative).</summary>
internal sealed class InventoryCostAllocationRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid LayerId { get; set; }
    public Guid MovementId { get; set; }
    public decimal Quantity { get; set; }
    public decimal Cost { get; set; }
    public long RecordedSequence { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

internal sealed class InventoryCostLayerConfiguration : IEntityTypeConfiguration<InventoryCostLayerRow>
{
    public void Configure(EntityTypeBuilder<InventoryCostLayerRow> builder)
    {
        builder.ToTable("InventoryCostLayers", "inventory", table =>
        {
            table.HasCheckConstraint("CK_InventoryCostLayers_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_InventoryCostLayers_Received", "[QuantityReceived] > 0 AND [ValueReceived] >= 0");
            table.HasCheckConstraint("CK_InventoryCostLayers_Remaining", "[QuantityRemaining] >= 0 AND [QuantityRemaining] <= [QuantityReceived] AND [ValueRemaining] >= 0 AND [ValueRemaining] <= [ValueReceived]");
            table.HasCheckConstraint("CK_InventoryCostLayers_Exhausted", "[QuantityRemaining] > 0 OR [ValueRemaining] = 0");
            table.HasCheckConstraint("CK_InventoryCostLayers_Utc", "DATEPART(TZOFFSET, [ReceivedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
        });
        builder.HasKey(static r => r.Id);
        builder.Property(static r => r.Id).ValueGeneratedNever();
        builder.Property(static r => r.QuantityReceived).HasPrecision(19, 4);
        builder.Property(static r => r.QuantityRemaining).HasPrecision(19, 4);
        builder.Property(static r => r.ValueReceived).HasPrecision(19, 4);
        builder.Property(static r => r.ValueRemaining).HasPrecision(19, 4);
        builder.Property(static r => r.ReceivedAtUtc).HasPrecision(7);
        builder.Property(static r => r.UpdatedAtUtc).HasPrecision(7);
        builder.HasIndex(static r => r.ReceiptMovementId).IsUnique();
        builder.HasIndex(static r => new { r.CompanyId, r.WarehouseId, r.ItemId, r.SourceSequence })
            .HasDatabaseName("IX_InventoryCostLayers_Open").HasFilter("[QuantityRemaining] > 0");
        builder.HasOne<StockMovementRow>().WithMany().HasForeignKey(static r => r.ReceiptMovementId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class InventoryCostAllocationConfiguration : IEntityTypeConfiguration<InventoryCostAllocationRow>
{
    public void Configure(EntityTypeBuilder<InventoryCostAllocationRow> builder)
    {
        builder.ToTable("InventoryCostAllocations", "inventory", table =>
        {
            table.HasCheckConstraint("CK_InventoryCostAllocations_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_InventoryCostAllocations_Amounts", "[Quantity] <> 0 AND SIGN([Quantity]) = SIGN([Cost]) OR ([Quantity] <> 0 AND [Cost] = 0)");
            table.HasCheckConstraint("CK_InventoryCostAllocations_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
            table.HasTrigger("TR_InventoryCostAllocations_Immutable");
            table.UseSqlOutputClause(false);
        });
        builder.HasKey(static r => r.Id);
        builder.Property(static r => r.Id).ValueGeneratedNever();
        builder.Property(static r => r.Quantity).HasPrecision(19, 4);
        builder.Property(static r => r.Cost).HasPrecision(19, 4);
        builder.Property(static r => r.CreatedAtUtc).HasPrecision(7);
        builder.HasIndex(static r => r.MovementId);
        builder.HasIndex(static r => r.LayerId);
        builder.HasOne<InventoryCostLayerRow>().WithMany().HasForeignKey(static r => r.LayerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StockMovementRow>().WithMany().HasForeignKey(static r => r.MovementId).OnDelete(DeleteBehavior.Restrict);
    }
}
