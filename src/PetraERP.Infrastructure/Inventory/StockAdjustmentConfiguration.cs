using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Security;

namespace PetraERP.Infrastructure.Inventory;

internal sealed class StockAdjustmentConfiguration : IEntityTypeConfiguration<StockAdjustmentRow>, IEntityTypeConfiguration<StockAdjustmentLineRow>
{
    public void Configure(EntityTypeBuilder<StockAdjustmentRow> builder)
    {
        builder.ToTable("StockAdjustments", "inventory", table =>
        {
            table.HasCheckConstraint("CK_StockAdjustments_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_StockAdjustments_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_StockAdjustments_AdjustmentNumber", StorageConventions.NormalizedRequired("[AdjustmentNumber]"));
            table.HasCheckConstraint("CK_StockAdjustments_Reason", StorageConventions.NormalizedRequired("[Reason]"));
            table.HasCheckConstraint("CK_StockAdjustments_Status", "[Status] IN (1, 2)");
            table.HasCheckConstraint("CK_StockAdjustments_AdjustmentDateUtc", "DATEPART(TZOFFSET, [AdjustmentDateUtc]) = 0");
            StorageConventions.UtcCheck(table, "CK_StockAdjustments_Utc");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.CompanyId).IsRequired();
        builder.Property(r => r.BranchId).IsRequired();
        builder.Property(r => r.WarehouseId).IsRequired();
        builder.Property(r => r.AdjustmentNumber).HasMaxLength(64).UseCollation(StorageConventions.OrdinalCollation).IsRequired();
        builder.Property(r => r.AdjustmentDateUtc).HasPrecision(7).IsRequired();
        builder.Property(r => r.Reason).HasMaxLength(200).IsRequired();
        builder.Property(r => r.Status).IsRequired();
        builder.Property(r => r.ApprovedByUserId);
        builder.Property(r => r.ApprovedAtUtc).HasPrecision(7);
        builder.Property(r => r.Notes).HasMaxLength(500);
        builder.Property(r => r.CreatedByUserId).IsRequired();

        StorageConventions.Tracking(builder);

        builder.HasIndex(r => new { r.CompanyId, r.AdjustmentNumber })
               .IsUnique()
               .HasDatabaseName("UQ_StockAdjustments_CompanyAdjustmentNumber");

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

        builder.HasOne<UserRow>()
               .WithMany()
               .HasForeignKey(r => r.ApprovedByUserId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UserRow>()
               .WithMany()
               .HasForeignKey(r => r.CreatedByUserId)
               .OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<StockAdjustmentLineRow> builder)
    {
        builder.ToTable("StockAdjustmentLines", "inventory", table =>
        {
            table.HasCheckConstraint("CK_StockAdjustmentLines_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_StockAdjustmentLines_CountedQuantity", "[CountedQuantity] >= 0");
            table.HasCheckConstraint("CK_StockAdjustmentLines_UnitCost", "[UnitCost] >= 0");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.AdjustmentId).IsRequired();
        builder.Property(r => r.ItemId).IsRequired();
        builder.Property(r => r.UnitId).IsRequired();
        builder.Property(r => r.SystemQuantity).HasPrecision(19, 4).IsRequired();
        builder.Property(r => r.CountedQuantity).HasPrecision(19, 4).IsRequired();
        builder.Property(r => r.DifferenceQuantity).HasPrecision(19, 4).IsRequired();
        builder.Property(r => r.UnitCost).HasPrecision(19, 4).IsRequired();

        builder.HasIndex(r => r.AdjustmentId)
               .HasDatabaseName("IX_StockAdjustmentLines_Adjustment");

        builder.HasOne(r => r.Adjustment)
               .WithMany(r => r.Lines)
               .HasForeignKey(r => r.AdjustmentId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ItemRow>()
               .WithMany()
               .HasForeignKey(r => r.ItemId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UnitOfMeasureRow>()
               .WithMany()
               .HasForeignKey(r => r.UnitId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
