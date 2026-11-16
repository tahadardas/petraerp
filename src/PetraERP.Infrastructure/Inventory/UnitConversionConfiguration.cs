using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Inventory;

internal sealed class UnitConversionConfiguration : IEntityTypeConfiguration<UnitConversionRow>
{
    public void Configure(EntityTypeBuilder<UnitConversionRow> builder)
    {
        builder.ToTable("UnitConversions", "inventory", table =>
        {
            table.HasCheckConstraint("CK_UnitConversions_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_UnitConversions_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_UnitConversions_Factor", "[Factor] > 0");
            table.HasCheckConstraint("CK_UnitConversions_DistinctUnits", "[FromUnitId] <> [ToUnitId]");
            StorageConventions.UtcCheck(table, "CK_UnitConversions_Utc");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.CompanyId).IsRequired();
        builder.Property(r => r.ItemId);
        builder.Property(r => r.FromUnitId).IsRequired();
        builder.Property(r => r.ToUnitId).IsRequired();
        builder.Property(r => r.Factor).HasPrecision(19, 6).IsRequired();

        StorageConventions.Tracking(builder);

        builder.HasIndex(r => new { r.CompanyId, r.ItemId, r.FromUnitId, r.ToUnitId })
               .IsUnique()
               .HasDatabaseName("UQ_UnitConversions_CompanyItemFromTo");

        builder.HasOne<CompanyRow>()
               .WithMany()
               .HasForeignKey(r => r.CompanyId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Item)
               .WithMany(r => r.Conversions)
               .HasForeignKey(r => r.ItemId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.FromUnit)
               .WithMany()
               .HasForeignKey(r => r.FromUnitId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.ToUnit)
               .WithMany()
               .HasForeignKey(r => r.ToUnitId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
