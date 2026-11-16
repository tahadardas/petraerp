using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Currencies;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Organizations;

internal sealed class CompanyAdditionalCurrencyConfiguration : IEntityTypeConfiguration<CompanyAdditionalCurrencyRow>
{
    public void Configure(EntityTypeBuilder<CompanyAdditionalCurrencyRow> builder)
    {
        builder.ToTable("CompanyAdditionalCurrencies", "system", table =>
        {
            StorageConventions.UtcCheck(table, "CK_CompanyAdditionalCurrencies_Utc");
            table.HasTrigger("TR_CompanyAdditionalCurrencies_BaseCurrency");
            table.UseSqlOutputClause(false);
        });
        builder.HasKey(static row => new { row.CompanyId, row.CurrencyCode });
        builder.Property(static row => row.CurrencyCode).HasColumnType("char(3)").UseCollation(StorageConventions.OrdinalCollation).IsRequired();
        builder.HasOne<CompanyRow>().WithMany().HasForeignKey(static row => row.CompanyId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<CurrencyRow>().WithMany().HasForeignKey(static row => row.CurrencyCode).OnDelete(DeleteBehavior.NoAction);
        StorageConventions.Tracking(builder);
    }
}
