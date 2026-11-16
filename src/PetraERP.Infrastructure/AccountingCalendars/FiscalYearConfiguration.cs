using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.AccountingCalendars;

internal sealed class FiscalYearConfiguration : IEntityTypeConfiguration<FiscalYearRow>
{
    public void Configure(EntityTypeBuilder<FiscalYearRow> builder)
    {
        builder.ToTable("FiscalYears", "ledger", table =>
        {
            table.HasCheckConstraint("CK_FiscalYears_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_FiscalYears_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_FiscalYears_Code", StorageConventions.NormalizedRequired("[Code]"));
            table.HasCheckConstraint("CK_FiscalYears_Range", "[RangeEnd] >= [RangeStart]");
            StorageConventions.UtcCheck(table, "CK_FiscalYears_Utc");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.CompanyId).IsRequired();
        builder.Property(r => r.Code).HasMaxLength(64).UseCollation(StorageConventions.OrdinalCollation).IsRequired();
        builder.Property(r => r.RangeStart).HasColumnType("date").IsRequired();
        builder.Property(r => r.RangeEnd).HasColumnType("date").IsRequired();

        StorageConventions.Tracking(builder);

        // Unique: one code per company
        builder.HasIndex(r => new { r.CompanyId, r.Code })
               .IsUnique()
               .HasDatabaseName("UQ_FiscalYears_CompanyCode");

        // Unique: non-overlapping years per company enforced by app logic +
        // a covering index; full overlap prevention requires a filtered index or
        // application-level serialized insertion — not an FK or simple CHECK.
        builder.HasIndex(r => new { r.CompanyId, r.RangeStart })
               .HasDatabaseName("IX_FiscalYears_CompanyStart");

        builder.HasOne<CompanyRow>()
               .WithMany()
               .HasForeignKey(r => r.CompanyId)
               .HasConstraintName("FK_FiscalYears_Companies")
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(r => r.Periods)
               .WithOne(p => p.FiscalYear)
               .HasForeignKey(p => p.FiscalYearId)
               .HasConstraintName("FK_AccountingPeriods_FiscalYears")
               .OnDelete(DeleteBehavior.Restrict);
    }
}
