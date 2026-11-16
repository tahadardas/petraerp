using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Security;

namespace PetraERP.Infrastructure.GeneralLedger;

internal sealed class CostCenterAuditConfiguration : IEntityTypeConfiguration<CostCenterAuditRow>
{
    public void Configure(EntityTypeBuilder<CostCenterAuditRow> builder)
    {
        builder.ToTable("CostCenterAudit", "ledger", table =>
        {
            table.HasCheckConstraint("CK_CostCenterAudit_Ids", "[EventId] <> '00000000-0000-0000-0000-000000000000' AND [CostCenterId] <> '00000000-0000-0000-0000-000000000000' AND [CorrelationId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_CostCenterAudit_Action", "[Action] IN (1,2,3,4)");
            table.HasCheckConstraint("CK_CostCenterAudit_Utc", "DATEPART(TZOFFSET,[OccurredAtUtc]) = 0");
        });
        builder.HasKey(row => row.EventId);
        builder.Property(row => row.EventId).ValueGeneratedNever();
        builder.Property(row => row.Action).HasColumnType("tinyint");
        builder.Property(row => row.OccurredAtUtc).HasPrecision(7);
        builder.HasIndex(row => new { row.CompanyId, row.CostCenterId, row.OccurredAtUtc, row.EventId }).HasDatabaseName("IX_CostCenterAudit_History");
        builder.HasOne<CompanyRow>().WithMany().HasForeignKey(row => row.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BranchRow>().WithMany().HasForeignKey(row => new { row.CompanyId, row.WorkingBranchId })
            .HasPrincipalKey(row => new { row.CompanyId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserRow>().WithMany().HasForeignKey(row => row.ActorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
