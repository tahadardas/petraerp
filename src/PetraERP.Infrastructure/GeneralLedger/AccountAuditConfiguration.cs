using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.GeneralLedger;

internal sealed class AccountAuditConfiguration : IEntityTypeConfiguration<AccountAuditRow>
{
    public void Configure(EntityTypeBuilder<AccountAuditRow> builder)
    {
        builder.ToTable("AccountAudit", "ledger", table =>
        {
            table.HasCheckConstraint("CK_AccountAudit_EventId", "[EventId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_AccountAudit_AccountId", "[AccountId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_AccountAudit_ActorUserId", "[ActorUserId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_AccountAudit_CorrelationId", "[CorrelationId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_AccountAudit_Action", "[Action] IN (1, 2, 3, 4, 5)");
            table.HasCheckConstraint("CK_AccountAudit_AccountType", "[AccountType] BETWEEN 1 AND 5");
            table.HasCheckConstraint("CK_AccountAudit_Reason", "([Reason] IS NULL OR (" + StorageConventions.NormalizedRequired("[Reason]") + ")) AND ([Action] <> 1 OR [Reason] IS NOT NULL)");
            table.HasCheckConstraint("CK_AccountAudit_Utc", "DATEPART(TZOFFSET, [OccurredAtUtc]) = 0 AND DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
        });

        builder.HasKey(r => r.EventId);
        builder.Property(r => r.EventId).ValueGeneratedNever();
        builder.Property(r => r.Action).HasColumnType("tinyint");
        builder.Property(r => r.AccountType).HasColumnType("tinyint");
        builder.Property(r => r.Code).HasMaxLength(64).UseCollation(StorageConventions.OrdinalCollation).IsRequired();
        builder.Property(r => r.NameArabic).HasMaxLength(200).IsRequired();
        builder.Property(r => r.NameEnglish).HasMaxLength(200);
        builder.Property(r => r.Reason).HasMaxLength(1000);
        builder.Property(r => r.OccurredAtUtc).HasPrecision(7);
        builder.Property(r => r.CreatedAtUtc).HasPrecision(7).HasDefaultValueSql("SYSUTCDATETIME()").ValueGeneratedOnAdd();

        builder.HasIndex(r => new { r.CompanyId, r.OccurredAtUtc }).HasDatabaseName("IX_AccountAudit_CompanyTime");
        builder.HasIndex(r => new { r.CompanyId, r.AccountId }).HasDatabaseName("IX_AccountAudit_CompanyAccount");
        builder.HasIndex(r => new { r.CompanyId, r.AccountId, r.OccurredAtUtc, r.EventId }).HasDatabaseName("IX_AccountAudit_AccountHistory");
        builder.HasOne<CompanyRow>().WithMany().HasForeignKey(r => r.CompanyId)
            .HasConstraintName("FK_AccountAudit_Companies").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BranchRow>().WithMany().HasForeignKey(r => new { r.CompanyId, r.WorkingBranchId })
            .HasPrincipalKey(branch => new { branch.CompanyId, branch.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
