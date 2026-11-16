using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Security;

namespace PetraERP.Infrastructure.Currencies;

internal sealed class ExchangeRateConfiguration : IEntityTypeConfiguration<ExchangeRateRow>
{
    public void Configure(EntityTypeBuilder<ExchangeRateRow> builder)
    {
        builder.ToTable("ExchangeRates", "system", table =>
        {
            table.HasCheckConstraint("CK_ExchangeRates_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_ExchangeRates_Rate", "[Rate] > 0");
            table.HasCheckConstraint("CK_ExchangeRates_Currencies", "[CurrencyCode] <> [BaseCurrencyCode]");
            table.HasCheckConstraint("CK_ExchangeRates_Source", StorageConventions.NormalizedRequired("[Source]"));
            table.HasCheckConstraint("CK_ExchangeRates_Reason", StorageConventions.NormalizedRequired("[Reason]"));
            StorageConventions.UtcCheck(table, "CK_ExchangeRates_Utc");
            table.HasTrigger("TR_ExchangeRates_AppendOnly");
            table.UseSqlOutputClause(false);
        });
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.CurrencyCode).HasColumnType("char(3)").UseCollation(StorageConventions.OrdinalCollation);
        builder.Property(r => r.BaseCurrencyCode).HasColumnType("char(3)").UseCollation(StorageConventions.OrdinalCollation);
        builder.Property(r => r.Rate).HasPrecision(19, 8);
        builder.Property(r => r.EffectiveDate).HasColumnType("date");
        builder.Property(r => r.Source).HasMaxLength(64);
        builder.Property(r => r.Reason).HasMaxLength(500);
        StorageConventions.Tracking(builder);

        builder.HasIndex(r => new { r.CompanyId, r.CurrencyCode, r.EffectiveDate, r.CreatedAtUtc })
            .HasDatabaseName("IX_ExchangeRates_CompanyCurrencyEffective");
        builder.HasOne<CompanyRow>().WithMany().HasForeignKey(r => r.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CurrencyRow>().WithMany().HasForeignKey(r => r.CurrencyCode).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CurrencyRow>().WithMany().HasForeignKey(r => r.BaseCurrencyCode).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserRow>().WithMany().HasForeignKey(r => r.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
