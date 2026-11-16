using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Inventory;

internal sealed class ItemBarcodeConfiguration : IEntityTypeConfiguration<ItemBarcodeRow>
{
    public void Configure(EntityTypeBuilder<ItemBarcodeRow> builder)
    {
        builder.ToTable("ItemBarcodes", "inventory", table =>
        {
            table.HasCheckConstraint("CK_ItemBarcodes_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_ItemBarcodes_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_ItemBarcodes_Barcode", StorageConventions.NormalizedRequired("[Barcode]"));
            StorageConventions.UtcCheck(table, "CK_ItemBarcodes_Utc");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.CompanyId).IsRequired();
        builder.Property(r => r.ItemId).IsRequired();
        builder.Property(r => r.UnitId).IsRequired();
        builder.Property(r => r.Barcode).HasMaxLength(64).UseCollation(StorageConventions.OrdinalCollation).IsRequired();
        builder.Property(r => r.IsDefault).IsRequired();

        StorageConventions.Tracking(builder);

        builder.HasIndex(r => new { r.CompanyId, r.Barcode })
               .IsUnique()
               .HasDatabaseName("UQ_ItemBarcodes_CompanyBarcode");

        builder.HasIndex(r => new { r.CompanyId, r.ItemId })
               .HasDatabaseName("IX_ItemBarcodes_CompanyItem");

        builder.HasOne<CompanyRow>()
               .WithMany()
               .HasForeignKey(r => r.CompanyId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Item)
               .WithMany(r => r.Barcodes)
               .HasForeignKey(r => r.ItemId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.Unit)
               .WithMany()
               .HasForeignKey(r => r.UnitId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
