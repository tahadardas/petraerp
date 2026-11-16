using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Inventory;

internal sealed class ItemConfiguration : IEntityTypeConfiguration<ItemRow>
{
    public void Configure(EntityTypeBuilder<ItemRow> builder)
    {
        builder.ToTable("Items", "inventory", table =>
        {
            table.HasCheckConstraint("CK_Items_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_Items_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_Items_Code", StorageConventions.NormalizedRequired("[Code]"));
            table.HasCheckConstraint("CK_Items_Names", StorageConventions.NormalizedNames);
            table.HasCheckConstraint("CK_Items_CostMethod", "[CostMethod] IN (1, 2)");
            table.HasCheckConstraint("CK_Items_MinStock", "[MinStock] IS NULL OR [MinStock] >= 0");
            table.HasCheckConstraint("CK_Items_MaxStock", "[MaxStock] IS NULL OR [MaxStock] >= 0");
            table.HasCheckConstraint("CK_Items_ReorderPoint", "[ReorderPoint] IS NULL OR [ReorderPoint] >= 0");
            table.HasCheckConstraint("CK_Items_StockLevels", "([MinStock] IS NULL OR [MaxStock] IS NULL) OR ([MinStock] <= [MaxStock])");
            table.HasCheckConstraint("CK_Items_ProfileUnits",
                "([Unit2Id] IS NULL AND [Unit2Factor] IS NULL AND [Unit2FactorVariable] = 0)"
                + " OR ([Unit2Id] IS NOT NULL AND [Unit2Id] <> [BaseUnitId] AND [Unit2Factor] > 0)");
            table.HasCheckConstraint("CK_Items_ProfileUnit3",
                "([Unit3Id] IS NULL AND [Unit3Factor] IS NULL AND [Unit3FactorVariable] = 0)"
                + " OR ([Unit3Id] IS NOT NULL AND [Unit2Id] IS NOT NULL AND [Unit3Id] <> [BaseUnitId] AND [Unit3Id] <> [Unit2Id] AND [Unit3Factor] > 0)");
            table.HasCheckConstraint("CK_Items_ProfileDefaultUnit",
                "[DefaultUnitIndex] IN (1, 2, 3) AND ([DefaultUnitIndex] <> 2 OR [Unit2Id] IS NOT NULL) AND ([DefaultUnitIndex] <> 3 OR [Unit3Id] IS NOT NULL)");
            table.HasCheckConstraint("CK_Items_ProfileGift",
                "([GiftQuantity] IS NULL OR [GiftQuantity] >= 0) AND ([GiftPerQuantity] IS NULL OR [GiftPerQuantity] >= 0)");
            table.HasCheckConstraint("CK_Items_ProfileKinds", "[ItemKind] = 1 AND [PricingPolicy] = 1");
            table.HasCheckConstraint("CK_Items_ProfileLastPurchaseRate", "[LastPurchaseRate] IS NULL OR [LastPurchaseRate] > 0");
            StorageConventions.UtcCheck(table, "CK_Items_Utc");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.CompanyId).IsRequired();
        builder.Property(r => r.GroupId);
        builder.Property(r => r.Code).HasMaxLength(64).UseCollation(StorageConventions.OrdinalCollation).IsRequired();
        builder.Property(r => r.NameArabic).HasMaxLength(200).IsRequired();
        builder.Property(r => r.NameEnglish).HasMaxLength(200);
        builder.Property(r => r.NormalizedSearchText).HasMaxLength(500).IsRequired();
        builder.Property(r => r.BaseUnitId).IsRequired();
        builder.Property(r => r.DefaultWarehouseId);
        builder.Property(r => r.CostMethod).IsRequired();
        builder.Property(r => r.MinStock).HasPrecision(19, 4);
        builder.Property(r => r.MaxStock).HasPrecision(19, 4);
        builder.Property(r => r.ReorderPoint).HasPrecision(19, 4);
        builder.Property(r => r.IsActive).IsRequired();

        builder.Property(r => r.Unit2Factor).HasPrecision(19, 8);
        builder.Property(r => r.Unit3Factor).HasPrecision(19, 8);
        builder.Property(r => r.DefaultUnitIndex).HasDefaultValue(1);
        builder.Property(r => r.GiftQuantity).HasPrecision(19, 4);
        builder.Property(r => r.GiftPerQuantity).HasPrecision(19, 4);
        builder.Property(r => r.Specifications).HasMaxLength(2000);
        builder.Property(r => r.Measure).HasMaxLength(100);
        builder.Property(r => r.Source).HasMaxLength(100);
        builder.Property(r => r.Location).HasMaxLength(100);
        builder.Property(r => r.Manufacturer).HasMaxLength(100);
        builder.Property(r => r.Color).HasMaxLength(100);
        builder.Property(r => r.OriginCountry).HasMaxLength(100);
        builder.Property(r => r.Quality).HasMaxLength(100);
        builder.Property(r => r.Model).HasMaxLength(100);
        builder.Property(r => r.ItemKind).HasDefaultValue(1);
        builder.Property(r => r.ImagePath).HasMaxLength(500);
        builder.Property(r => r.PricingPolicy).HasDefaultValue(1);
        builder.Property(r => r.LastPurchaseRate).HasPrecision(19, 8);

        StorageConventions.Tracking(builder);

        builder.HasIndex(r => new { r.CompanyId, r.Code })
               .IsUnique()
               .HasDatabaseName("UQ_Items_CompanyCode");

        builder.HasIndex(r => new { r.CompanyId, r.GroupId, r.IsActive })
               .HasDatabaseName("IX_Items_CompanyGroupActive");

        builder.HasIndex(r => new { r.CompanyId, r.NormalizedSearchText })
               .HasDatabaseName("IX_Items_CompanyNormalizedSearchText");

        builder.HasOne<CompanyRow>()
               .WithMany()
               .HasForeignKey(r => r.CompanyId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Group)
               .WithMany(r => r.Items)
               .HasForeignKey(r => r.GroupId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.BaseUnit)
               .WithMany()
               .HasForeignKey(r => r.BaseUnitId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.DefaultWarehouse)
               .WithMany()
               .HasForeignKey(r => r.DefaultWarehouseId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UnitOfMeasureRow>()
               .WithMany()
               .HasForeignKey(r => r.Unit2Id)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UnitOfMeasureRow>()
               .WithMany()
               .HasForeignKey(r => r.Unit3Id)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
