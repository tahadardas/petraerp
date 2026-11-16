using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Parties;
using PetraERP.Infrastructure.Patterns;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Inventory;

internal sealed class ItemPriceConfiguration : IEntityTypeConfiguration<ItemPriceRow>
{
    public void Configure(EntityTypeBuilder<ItemPriceRow> builder)
    {
        builder.ToTable("ItemPrices", "inventory", table =>
        {
            table.HasCheckConstraint("CK_ItemPrices_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_ItemPrices_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_ItemPrices_PriceListCode", StorageConventions.NormalizedRequired("[PriceListCode]"));
            table.HasCheckConstraint("CK_ItemPrices_Price", "[Price] >= 0");
            table.HasCheckConstraint("CK_ItemPrices_MinQuantity", "[MinQuantity] >= 0");
            table.HasCheckConstraint("CK_ItemPrices_Validity", "[ValidFrom] IS NULL OR [ValidTo] IS NULL OR [ValidTo] >= [ValidFrom]");
            StorageConventions.UtcCheck(table, "CK_ItemPrices_Utc");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.CompanyId).IsRequired();
        builder.Property(r => r.ItemId).IsRequired();
        builder.Property(r => r.UnitId).IsRequired();
        builder.Property(r => r.PriceListCode).HasMaxLength(64).UseCollation(StorageConventions.OrdinalCollation).IsRequired();
        builder.Property(r => r.Price).HasPrecision(19, 4).IsRequired();
        builder.Property(r => r.CurrencyCode).HasMaxLength(3).UseCollation(StorageConventions.OrdinalCollation).IsRequired();
        builder.Property(r => r.MinQuantity).HasPrecision(19, 4).IsRequired();

        StorageConventions.Tracking(builder);

        builder.Property(r => r.ValidFrom).HasColumnType("date");
        builder.Property(r => r.ValidTo).HasColumnType("date");

        // NULL party/pattern/start compare equal in a SQL Server unique index, so each scope has one row per break and start.
        builder.HasIndex(r => new { r.CompanyId, r.ItemId, r.UnitId, r.PriceListCode, r.CurrencyCode, r.PartyId, r.PatternId, r.MinQuantity, r.ValidFrom })
               .IsUnique()
               .HasFilter(null)
               .HasDatabaseName("UQ_ItemPrices_Scope");

        builder.HasOne<PartyRow>().WithMany().HasForeignKey(r => r.PartyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DocumentPatternRow>().WithMany().HasForeignKey(r => r.PatternId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<CompanyRow>()
               .WithMany()
               .HasForeignKey(r => r.CompanyId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Item)
               .WithMany(r => r.Prices)
               .HasForeignKey(r => r.ItemId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.Unit)
               .WithMany()
               .HasForeignKey(r => r.UnitId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
