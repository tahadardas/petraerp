using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Inventory;

internal sealed class UnitOfMeasureConfiguration : IEntityTypeConfiguration<UnitOfMeasureRow>
{
    public void Configure(EntityTypeBuilder<UnitOfMeasureRow> builder)
    {
        builder.ToTable("UnitsOfMeasure", "inventory", table =>
        {
            table.HasCheckConstraint("CK_UnitsOfMeasure_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_UnitsOfMeasure_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_UnitsOfMeasure_Code", StorageConventions.NormalizedRequired("[Code]"));
            table.HasCheckConstraint("CK_UnitsOfMeasure_Names", StorageConventions.NormalizedNames);
            table.HasCheckConstraint("CK_UnitsOfMeasure_Symbol", StorageConventions.NormalizedRequired("[Symbol]"));
            table.HasCheckConstraint("CK_UnitsOfMeasure_DecimalPlaces", "[DecimalPlaces] BETWEEN 0 AND 4");
            table.HasCheckConstraint("CK_UnitsOfMeasure_Dimension", "[Dimension] BETWEEN 0 AND 7");
            StorageConventions.UtcCheck(table, "CK_UnitsOfMeasure_Utc");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.CompanyId).IsRequired();
        builder.Property(r => r.Code).HasMaxLength(64).UseCollation(StorageConventions.OrdinalCollation).IsRequired();
        builder.Property(r => r.NameArabic).HasMaxLength(200).IsRequired();
        builder.Property(r => r.NameEnglish).HasMaxLength(200);
        builder.Property(r => r.Symbol).HasMaxLength(32).IsRequired();
        builder.Property(r => r.DecimalPlaces).IsRequired();
        builder.Property(r => r.Dimension).HasDefaultValue((byte)0).IsRequired();
        builder.Property(r => r.IsActive).IsRequired();

        StorageConventions.Tracking(builder);

        builder.HasIndex(r => new { r.CompanyId, r.Code })
               .IsUnique()
               .HasDatabaseName("UQ_UnitsOfMeasure_CompanyCode");

        builder.HasOne<CompanyRow>()
               .WithMany()
               .HasForeignKey(r => r.CompanyId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
