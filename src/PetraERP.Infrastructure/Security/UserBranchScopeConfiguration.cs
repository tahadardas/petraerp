using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Organizations;

namespace PetraERP.Infrastructure.Security;

internal sealed class UserBranchScopeConfiguration : IEntityTypeConfiguration<UserBranchScopeRow>
{
    public void Configure(EntityTypeBuilder<UserBranchScopeRow> builder)
    {
        builder.ToTable("UserBranchScopes", SecurityStorage.Schema, table =>
            table.HasCheckConstraint("CK_UserBranchScopes_Utc", SecurityStorage.Utc("[CreatedAtUtc]")));
        builder.HasKey(static row => new { row.UserId, row.CompanyId, row.BranchId });
        builder.Property(static row => row.CreatedAtUtc).HasPrecision(7).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.HasIndex(static row => new { row.CompanyId, row.BranchId });

        // A branch grant requires the company grant of the same user.
        builder.HasOne<UserCompanyScopeRow>().WithMany()
            .HasForeignKey(static row => new { row.UserId, row.CompanyId })
            .OnDelete(DeleteBehavior.NoAction);

        // The branch must belong to that company (composite reference to the branch's company-scoped key).
        builder.HasOne<BranchRow>().WithMany()
            .HasForeignKey(static row => new { row.CompanyId, row.BranchId })
            .HasPrincipalKey(static branch => new { branch.CompanyId, branch.Id })
            .OnDelete(DeleteBehavior.NoAction);
    }
}
