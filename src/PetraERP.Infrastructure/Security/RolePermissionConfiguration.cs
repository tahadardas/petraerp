using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Security;

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermissionRow>
{
    public void Configure(EntityTypeBuilder<RolePermissionRow> builder)
    {
        builder.ToTable("RolePermissions", SecurityStorage.Schema, table =>
            table.HasCheckConstraint("CK_RolePermissions_Utc", SecurityStorage.Utc("[CreatedAtUtc]")));
        builder.HasKey(static row => new { row.RoleId, row.PermissionCode });
        builder.Property(static row => row.PermissionCode).HasMaxLength(128).UseCollation(StorageConventions.OrdinalCollation);
        builder.Property(static row => row.CreatedAtUtc).HasPrecision(7).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.HasIndex(static row => row.PermissionCode);
        builder.HasOne<RoleRow>().WithMany().HasForeignKey(static row => row.RoleId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<PermissionRow>().WithMany().HasForeignKey(static row => row.PermissionCode).OnDelete(DeleteBehavior.NoAction);
    }
}
