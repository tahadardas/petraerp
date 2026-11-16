using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.AccountingCalendars;

internal sealed class AccountingPeriodConfiguration : IEntityTypeConfiguration<AccountingPeriodRow>
{
    public void Configure(EntityTypeBuilder<AccountingPeriodRow> builder)
    {
        builder.ToTable("AccountingPeriods", "ledger", table =>
        {
            table.HasCheckConstraint("CK_AccountingPeriods_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_AccountingPeriods_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_AccountingPeriods_FiscalYearId", "[FiscalYearId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_AccountingPeriods_Code", StorageConventions.NormalizedRequired("[Code]"));
            table.HasCheckConstraint("CK_AccountingPeriods_Range", "[RangeEnd] >= [RangeStart]");
            table.HasCheckConstraint("CK_AccountingPeriods_State", "[State] IN (0, 1, 2)");
            StorageConventions.UtcCheck(table, "CK_AccountingPeriods_Utc");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.CompanyId).IsRequired();
        builder.Property(r => r.FiscalYearId).IsRequired();
        builder.Property(r => r.Code).HasMaxLength(64).UseCollation(StorageConventions.OrdinalCollation).IsRequired();
        builder.Property(r => r.RangeStart).HasColumnType("date").IsRequired();
        builder.Property(r => r.RangeEnd).HasColumnType("date").IsRequired();
        builder.Property(r => r.State).HasColumnType("tinyint").IsRequired();

        StorageConventions.Tracking(builder);

        // Unique code per fiscal year
        builder.HasIndex(r => new { r.FiscalYearId, r.Code })
               .IsUnique()
               .HasDatabaseName("UQ_AccountingPeriods_YearCode");

        // Covering index for period lookup by company + date
        builder.HasIndex(r => new { r.CompanyId, r.RangeStart, r.RangeEnd })
               .HasDatabaseName("IX_AccountingPeriods_CompanyDateRange");

        // CompanyId FK for cross-table scoping checks
        builder.HasOne<CompanyRow>()
               .WithMany()
               .HasForeignKey(r => r.CompanyId)
               .HasConstraintName("FK_AccountingPeriods_Companies")
               .OnDelete(DeleteBehavior.Restrict);

        // FiscalYearId FK is configured on the FiscalYearConfiguration (parent side)
        builder.HasOne(r => r.FiscalYear)
               .WithMany(y => y.Periods)
               .HasForeignKey(r => r.FiscalYearId)
               .HasConstraintName("FK_AccountingPeriods_FiscalYears")
               .OnDelete(DeleteBehavior.Restrict);
    }
}
