using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Security;

namespace PetraERP.Infrastructure.Inventory;

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovementRow>
{
    public void Configure(EntityTypeBuilder<StockMovementRow> builder)
    {
        builder.ToTable("StockMovements", "inventory", table =>
        {
            table.HasCheckConstraint("CK_StockMovements_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_StockMovements_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_StockMovements_BranchId", "[BranchId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_StockMovements_WarehouseId", "[WarehouseId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_StockMovements_ItemId", "[ItemId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_StockMovements_UnitId", "[UnitId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_StockMovements_MovementKind", "[MovementKind] BETWEEN 1 AND 10");
            table.HasCheckConstraint("CK_StockMovements_Quantity", "[Quantity] > 0");
            table.HasCheckConstraint("CK_StockMovements_BaseQuantity", "[BaseQuantity] <> 0");
            table.HasCheckConstraint("CK_StockMovements_UnitCost", "[UnitCost] >= 0");
            table.HasCheckConstraint("CK_StockMovements_TotalCost", "[TotalCost] >= 0");
            table.HasCheckConstraint("CK_StockMovements_InternalIssueCostCenter", "([MovementKind] <> 10) OR ([CostCenterId] IS NOT NULL)");
            table.HasCheckConstraint("CK_StockMovements_Utc", "DATEPART(TZOFFSET, [MovementDateUtc]) = 0 AND DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
            // An authorized negative issue keeps quantity and value on the same side of zero. A positive quantity can
            // never carry negative value (or vice versa), so the ledger stays rebuildable even below zero.
            table.HasCheckConstraint("CK_StockMovements_AuditBalance", "([QuantityBefore] IS NULL AND [QuantityAfter] IS NULL AND [ValueBefore] IS NULL AND [ValueAfter] IS NULL) OR ([QuantityBefore] IS NOT NULL AND [QuantityAfter] IS NOT NULL AND [ValueBefore] IS NOT NULL AND [ValueAfter] IS NOT NULL AND (([QuantityBefore]>0 AND [ValueBefore]>=0) OR ([QuantityBefore]<0 AND [ValueBefore]<=0) OR ([QuantityBefore]=0 AND [ValueBefore]=0)) AND (([QuantityAfter]>0 AND [ValueAfter]>=0) OR ([QuantityAfter]<0 AND [ValueAfter]<=0) OR ([QuantityAfter]=0 AND [ValueAfter]=0)) AND [QuantityAfter]=[QuantityBefore]+[BaseQuantity] AND [ValueAfter]=[ValueBefore]+CASE WHEN [BaseQuantity]<0 THEN -[TotalCost] ELSE [TotalCost] END AND [RecordedAtUtc] IS NOT NULL)");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.RecordedSequence).ValueGeneratedNever();
        builder.HasIndex(r => r.RecordedSequence).IsUnique();
        builder.Property(r => r.RecordedAtUtc).HasPrecision(7);
        builder.Property(r => r.QuantityBefore).HasPrecision(19, 4);
        builder.Property(r => r.QuantityAfter).HasPrecision(19, 4);
        builder.Property(r => r.ValueBefore).HasPrecision(19, 4);
        builder.Property(r => r.ValueAfter).HasPrecision(19, 4);
        builder.HasIndex(r => r.ReversesMovementId).IsUnique().HasFilter("[ReversesMovementId] IS NOT NULL");
        builder.HasOne<StockMovementRow>().WithMany().HasForeignKey(r => r.ReversesMovementId).OnDelete(DeleteBehavior.Restrict);

        builder.Property(r => r.CompanyId).IsRequired();
        builder.Property(r => r.BranchId).IsRequired();
        builder.Property(r => r.WarehouseId).IsRequired();
        builder.Property(r => r.ItemId).IsRequired();
        builder.Property(r => r.UnitId).IsRequired();
        builder.Property(r => r.MovementKind).IsRequired();
        builder.Property(r => r.Quantity).HasPrecision(19, 4).IsRequired();
        builder.Property(r => r.BaseQuantity).HasPrecision(19, 4).IsRequired();
        builder.Property(r => r.UnitCost).HasPrecision(19, 4).IsRequired();
        builder.Property(r => r.TotalCost).HasPrecision(19, 4).IsRequired();
        builder.Property(r => r.MovementDateUtc).HasPrecision(7).IsRequired();
        builder.Property(r => r.DocumentType).HasMaxLength(64).UseCollation(StorageConventions.OrdinalCollation).IsRequired();
        builder.Property(r => r.DocumentId);
        builder.Property(r => r.DocumentLineId);
        builder.Property(r => r.DocumentNumber).HasMaxLength(64);
        builder.Property(r => r.CostCenterId);
        builder.Property(r => r.Notes).HasMaxLength(500);
        builder.Property(r => r.CreatedByUserId).IsRequired();
        builder.Property(r => r.CreatedAtUtc).HasPrecision(7).HasDefaultValueSql("SYSUTCDATETIME()").IsRequired();

        builder.HasIndex(r => new { r.CompanyId, r.WarehouseId, r.ItemId })
               .HasDatabaseName("IX_StockMovements_CompanyWarehouseItem");

        builder.HasIndex(r => new { r.CompanyId, r.ItemId, r.MovementDateUtc })
               .HasDatabaseName("IX_StockMovements_CompanyItemDate");

        builder.HasIndex(r => new { r.CompanyId, r.DocumentType, r.DocumentId })
               .HasDatabaseName("IX_StockMovements_CompanyDocument");

        // A document line produces at most one movement of its document type (original cost lookup for returns).
        builder.HasIndex(r => new { r.CompanyId, r.DocumentType, r.DocumentLineId })
               .IsUnique()
               .HasFilter("[DocumentLineId] IS NOT NULL")
               .HasDatabaseName("UQ_StockMovements_DocumentLine");

        builder.HasOne<CompanyRow>()
               .WithMany()
               .HasForeignKey(r => r.CompanyId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<BranchRow>()
               .WithMany()
               .HasForeignKey(r => r.BranchId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<WarehouseRow>()
               .WithMany()
               .HasForeignKey(r => r.WarehouseId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ItemRow>()
               .WithMany()
               .HasForeignKey(r => r.ItemId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UnitOfMeasureRow>()
               .WithMany()
               .HasForeignKey(r => r.UnitId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<CostCenterRow>()
               .WithMany()
               .HasForeignKey(r => r.CostCenterId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UserRow>()
               .WithMany()
               .HasForeignKey(r => r.CreatedByUserId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
