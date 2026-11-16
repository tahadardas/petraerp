using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PetraERP.Infrastructure.Security;

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRoleRow>
{
    public void Configure(EntityTypeBuilder<UserRoleRow> builder)
    {
        builder.ToTable("UserRoles", SecurityStorage.Schema, table =>
            table.HasCheckConstraint("CK_UserRoles_Utc", SecurityStorage.Utc("[CreatedAtUtc]")));
        builder.HasKey(static row => new { row.UserId, row.RoleId });
        builder.Property(static row => row.CreatedAtUtc).HasPrecision(7).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.HasIndex(static row => row.RoleId);
        builder.HasOne<UserRow>().WithMany().HasForeignKey(static row => row.UserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<RoleRow>().WithMany().HasForeignKey(static row => row.RoleId).OnDelete(DeleteBehavior.NoAction);
    }
}
