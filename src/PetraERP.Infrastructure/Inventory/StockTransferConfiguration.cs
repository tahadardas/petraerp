using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Security;

namespace PetraERP.Infrastructure.Inventory;

internal sealed class StockTransferConfiguration : IEntityTypeConfiguration<StockTransferRow>, IEntityTypeConfiguration<StockTransferLineRow>
{
    public void Configure(EntityTypeBuilder<StockTransferRow> builder)
    {
        builder.ToTable("StockTransfers", "inventory", table =>
        {
            table.HasCheckConstraint("CK_StockTransfers_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_StockTransfers_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_StockTransfers_TransferNumber", StorageConventions.NormalizedRequired("[TransferNumber]"));
            table.HasCheckConstraint("CK_StockTransfers_DistinctWarehouses", "[SourceWarehouseId] <> [TargetWarehouseId]");
            table.HasCheckConstraint("CK_StockTransfers_Status", "[Status] IN (1, 2, 3)");
            table.HasCheckConstraint("CK_StockTransfers_TransferDateUtc", "DATEPART(TZOFFSET, [TransferDateUtc]) = 0");
            StorageConventions.UtcCheck(table, "CK_StockTransfers_Utc");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.CompanyId).IsRequired();
        builder.Property(r => r.TransferNumber).HasMaxLength(64).UseCollation(StorageConventions.OrdinalCollation).IsRequired();
        builder.Property(r => r.SourceBranchId).IsRequired();
        builder.Property(r => r.SourceWarehouseId).IsRequired();
        builder.Property(r => r.TargetBranchId).IsRequired();
        builder.Property(r => r.TargetWarehouseId).IsRequired();
        builder.Property(r => r.TransferDateUtc).HasPrecision(7).IsRequired();
        builder.Property(r => r.Status).IsRequired();
        builder.Property(r => r.Notes).HasMaxLength(500);
        builder.Property(r => r.CreatedByUserId).IsRequired();

        StorageConventions.Tracking(builder);

        builder.HasIndex(r => new { r.CompanyId, r.TransferNumber })
               .IsUnique()
               .HasDatabaseName("UQ_StockTransfers_CompanyTransferNumber");

        builder.HasOne<CompanyRow>()
               .WithMany()
               .HasForeignKey(r => r.CompanyId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<BranchRow>()
               .WithMany()
               .HasForeignKey(r => r.SourceBranchId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<WarehouseRow>()
               .WithMany()
               .HasForeignKey(r => r.SourceWarehouseId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<BranchRow>()
               .WithMany()
               .HasForeignKey(r => r.TargetBranchId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<WarehouseRow>()
               .WithMany()
               .HasForeignKey(r => r.TargetWarehouseId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UserRow>()
               .WithMany()
               .HasForeignKey(r => r.CreatedByUserId)
               .OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<StockTransferLineRow> builder)
    {
        builder.ToTable("StockTransferLines", "inventory", table =>
        {
            table.HasCheckConstraint("CK_StockTransferLines_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_StockTransferLines_Quantity", "[Quantity] > 0");
            table.HasCheckConstraint("CK_StockTransferLines_BaseQuantity", "[BaseQuantity] > 0");
            table.HasCheckConstraint("CK_StockTransferLines_UnitCost", "[UnitCost] >= 0");
            table.HasCheckConstraint("CK_StockTransferLines_TotalCost", "[TotalCost] >= 0");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.TransferId).IsRequired();
        builder.Property(r => r.ItemId).IsRequired();
        builder.Property(r => r.UnitId).IsRequired();
        builder.Property(r => r.Quantity).HasPrecision(19, 4).IsRequired();
        builder.Property(r => r.BaseQuantity).HasPrecision(19, 4).IsRequired();
        builder.Property(r => r.UnitCost).HasPrecision(19, 4).IsRequired();
        builder.Property(r => r.TotalCost).HasPrecision(19, 4).IsRequired();

        builder.HasIndex(r => r.TransferId)
               .HasDatabaseName("IX_StockTransferLines_Transfer");

        builder.HasOne(r => r.Transfer)
               .WithMany(r => r.Lines)
               .HasForeignKey(r => r.TransferId)
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
