using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.AccountingCalendars;

internal sealed class PeriodAuditConfiguration : IEntityTypeConfiguration<PeriodAuditRow>
{
    public void Configure(EntityTypeBuilder<PeriodAuditRow> builder)
    {
        builder.ToTable("PeriodAudit", "ledger", table =>
        {
            table.HasCheckConstraint("CK_PeriodAudit_EventId", "[EventId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_PeriodAudit_ActorUserId", "[ActorUserId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_PeriodAudit_CorrelationId", "[CorrelationId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_PeriodAudit_States", "[PreviousState] IN (0, 1, 2) AND [NewState] IN (0, 1, 2) AND [PreviousState] <> [NewState]");
            table.HasCheckConstraint("CK_PeriodAudit_Reason", StorageConventions.NormalizedRequired("[Reason]"));
            table.HasCheckConstraint("CK_PeriodAudit_Utc", "DATEPART(TZOFFSET, [OccurredAtUtc]) = 0 AND DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
        });

        builder.HasKey(r => r.EventId);
        builder.Property(r => r.EventId).ValueGeneratedNever();
        builder.Property(r => r.PreviousState).HasColumnType("tinyint");
        builder.Property(r => r.NewState).HasColumnType("tinyint");
        builder.Property(r => r.Reason).HasMaxLength(1000).IsRequired();
        builder.Property(r => r.OccurredAtUtc).HasPrecision(7);
        builder.Property(r => r.CreatedAtUtc).HasPrecision(7).HasDefaultValueSql("SYSUTCDATETIME()").ValueGeneratedOnAdd();

        builder.HasIndex(r => new { r.CompanyId, r.PeriodId, r.OccurredAtUtc }).HasDatabaseName("IX_PeriodAudit_CompanyPeriodTime");
        builder.HasOne<CompanyRow>().WithMany().HasForeignKey(r => r.CompanyId)
            .HasConstraintName("FK_PeriodAudit_Companies").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FiscalYearRow>().WithMany().HasForeignKey(r => r.FiscalYearId)
            .HasConstraintName("FK_PeriodAudit_FiscalYears").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AccountingPeriodRow>().WithMany().HasForeignKey(r => r.PeriodId)
            .HasConstraintName("FK_PeriodAudit_AccountingPeriods").OnDelete(DeleteBehavior.Restrict);
    }
}
