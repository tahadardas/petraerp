using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Parties;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Security;

namespace PetraERP.Infrastructure.Sales;

internal sealed class SalesCreditAuditConfiguration : IEntityTypeConfiguration<SalesCreditAuditRow>
{
    public void Configure(EntityTypeBuilder<SalesCreditAuditRow> builder)
    {
        builder.ToTable("CreditAudit", "sales", table =>
        {
            table.HasTrigger("TR_CreditAudit_Immutable");
            table.UseSqlOutputClause(false);
            table.HasCheckConstraint("CK_SalesCreditAudit_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_SalesCreditAudit_Amounts", "[CreditLimitBase] >= 0 AND [InvoiceBaseAmount] >= 0 AND [ExposureAfterBase] = [ExposureBeforeBase] + [InvoiceBaseAmount]");
            table.HasCheckConstraint("CK_SalesCreditAudit_Policy", "[PolicyKind] IN (1,2,3) AND ([WasCreditBlocked] = 0 OR [IsOverride] = 1)");
            table.HasCheckConstraint("CK_SalesCreditAudit_Reason", "LEN(LTRIM(RTRIM([Reason]))) > 0");
            table.HasCheckConstraint("CK_SalesCreditAudit_Utc", "DATEPART(TZOFFSET,[RecordedAtUtc]) = 0");
        });
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Reason).HasMaxLength(500).IsRequired();
        builder.Property(r => r.RecordedAtUtc).HasPrecision(7);
        builder.Property(r => r.CreditLimitBase).HasPrecision(19,4);
        builder.Property(r => r.ExposureBeforeBase).HasPrecision(19,4);
        builder.Property(r => r.InvoiceBaseAmount).HasPrecision(19,4);
        builder.Property(r => r.ExposureAfterBase).HasPrecision(19,4);
        builder.HasIndex(r => new { r.CompanyId, r.BranchId, r.InvoiceId }).IsUnique();
        builder.HasOne<CompanyRow>().WithMany().HasForeignKey(r => r.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BranchRow>().WithMany().HasForeignKey(r => r.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SalesInvoiceRow>().WithMany().HasForeignKey(r => r.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PartyRow>().WithMany().HasForeignKey(r => r.PartyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserRow>().WithMany().HasForeignKey(r => r.ActorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
