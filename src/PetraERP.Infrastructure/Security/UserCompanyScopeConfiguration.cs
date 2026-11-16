using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Organizations;

namespace PetraERP.Infrastructure.Security;

internal sealed class UserCompanyScopeConfiguration : IEntityTypeConfiguration<UserCompanyScopeRow>
{
    public void Configure(EntityTypeBuilder<UserCompanyScopeRow> builder)
    {
        builder.ToTable("UserCompanyScopes", SecurityStorage.Schema, table =>
            table.HasCheckConstraint("CK_UserCompanyScopes_Utc", SecurityStorage.Utc("[CreatedAtUtc]")));
        builder.HasKey(static row => new { row.UserId, row.CompanyId });
        builder.Property(static row => row.CreatedAtUtc).HasPrecision(7).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.HasIndex(static row => row.CompanyId);
        builder.HasOne<UserRow>().WithMany().HasForeignKey(static row => row.UserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<CompanyRow>().WithMany().HasForeignKey(static row => row.CompanyId).OnDelete(DeleteBehavior.NoAction);
    }
}
