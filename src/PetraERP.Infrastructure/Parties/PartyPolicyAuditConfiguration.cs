using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Security;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Parties;

internal sealed class PartyPolicyAuditConfiguration : IEntityTypeConfiguration<PartyPolicyAuditRow>
{
    public void Configure(EntityTypeBuilder<PartyPolicyAuditRow> builder)
    {
        builder.ToTable("PolicyAudit", "parties", table =>
        {
            table.HasTrigger("TR_PolicyAudit_Immutable");
            table.UseSqlOutputClause(false);
            table.HasCheckConstraint("CK_PartyPolicyAudit_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_PartyPolicyAudit_Limits", "[BeforeCreditLimit]>=0 AND [AfterCreditLimit]>=0");
            table.HasCheckConstraint("CK_PartyPolicyAudit_Policies", "[BeforePolicy] IN (1,2,3) AND [AfterPolicy] IN (1,2,3)");
            table.HasCheckConstraint("CK_PartyPolicyAudit_Utc", "DATEPART(TZOFFSET,[DateUtc])=0");
        });
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.BeforeCreditLimit).HasPrecision(19,4);
        builder.Property(r => r.AfterCreditLimit).HasPrecision(19,4);
        builder.Property(r => r.DateUtc).HasPrecision(7);
        builder.HasIndex(r => new { r.CompanyId, r.PartyId, r.DateUtc });
        builder.HasOne<CompanyRow>().WithMany().HasForeignKey(r => r.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PartyRow>().WithMany().HasForeignKey(r => r.PartyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserRow>().WithMany().HasForeignKey(r => r.ActorId).OnDelete(DeleteBehavior.Restrict);
    }
}
