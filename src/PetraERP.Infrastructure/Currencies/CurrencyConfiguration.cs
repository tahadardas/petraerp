using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Currencies;

internal sealed class CurrencyConfiguration : IEntityTypeConfiguration<CurrencyRow>
{
    public void Configure(EntityTypeBuilder<CurrencyRow> builder)
    {
        builder.ToTable("Currencies", "system", table =>
        {
            table.HasCheckConstraint("CK_Currencies_Code", "[Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z]%'");
            table.HasCheckConstraint("CK_Currencies_Names", StorageConventions.NormalizedNames);
            table.HasCheckConstraint("CK_Currencies_DecimalPlaces", "[DecimalPlaces] BETWEEN 0 AND 4");
            table.HasCheckConstraint("CK_Currencies_RoundingMode", "[RoundingMode] IN (1, 2, 3, 4)");
            StorageConventions.UtcCheck(table, "CK_Currencies_Utc");
        });
        builder.HasKey(static row => row.Code);
        builder.Property(static row => row.Code).HasColumnType("char(3)").UseCollation(StorageConventions.OrdinalCollation);
        builder.Property(static row => row.NameArabic).HasMaxLength(200).IsRequired();
        builder.Property(static row => row.NameEnglish).HasMaxLength(200);
        builder.Property(static row => row.Symbol).HasMaxLength(8);
        builder.Property(static row => row.MinorUnitNameArabic).HasMaxLength(100);
        builder.Property(static row => row.MinorUnitNameEnglish).HasMaxLength(100);
        builder.Property(static row => row.DecimalPlaces).HasColumnType("tinyint").HasDefaultValue((byte)2);
        builder.Property(static row => row.RoundingMode).HasColumnType("tinyint").HasDefaultValue((byte)1);
        StorageConventions.Tracking(builder);
    }
}
