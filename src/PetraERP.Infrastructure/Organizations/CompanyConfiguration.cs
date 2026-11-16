using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Currencies;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Organizations;

internal sealed class CompanyConfiguration : IEntityTypeConfiguration<CompanyRow>
{
    public void Configure(EntityTypeBuilder<CompanyRow> builder)
    {
        builder.ToTable("Companies", "system", table =>
        {
            table.HasCheckConstraint("CK_Companies_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_Companies_Code", StorageConventions.NormalizedRequired("[Code]"));
            table.HasCheckConstraint("CK_Companies_Names", StorageConventions.NormalizedNames);
            StorageConventions.UtcCheck(table, "CK_Companies_Utc");
            table.HasTrigger("TR_Companies_BaseCurrency");
            table.UseSqlOutputClause(false);
        });
        builder.HasKey(static row => row.Id);
        builder.Property(static row => row.Id).ValueGeneratedNever();
        builder.Property(static row => row.Code).HasMaxLength(64).UseCollation(StorageConventions.OrdinalCollation).IsRequired();
        builder.HasIndex(static row => row.Code).IsUnique();
        builder.Property(static row => row.NameArabic).HasMaxLength(200).IsRequired();
        builder.Property(static row => row.NameEnglish).HasMaxLength(200);
        builder.Property(static row => row.BaseCurrencyCode).HasColumnType("char(3)").UseCollation(StorageConventions.OrdinalCollation).IsRequired();
        builder.HasOne<CurrencyRow>().WithMany().HasForeignKey(static row => row.BaseCurrencyCode).OnDelete(DeleteBehavior.NoAction);
        StorageConventions.Tracking(builder);
    }
}
