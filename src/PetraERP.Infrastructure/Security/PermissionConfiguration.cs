using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Application.Security;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Security;

internal sealed class PermissionConfiguration : IEntityTypeConfiguration<PermissionRow>
{
    public void Configure(EntityTypeBuilder<PermissionRow> builder)
    {
        builder.ToTable("Permissions", SecurityStorage.Schema, table =>
            table.HasCheckConstraint("CK_Permissions_Code",
                $"LEN([Code]) BETWEEN 3 AND 128 AND {SecurityStorage.AsciiIdentifier("[Code]", "A-Za-z.")}"));
        builder.HasKey(static row => row.Code);
        builder.Property(static row => row.Code).HasMaxLength(128).UseCollation(StorageConventions.OrdinalCollation);

        // The catalog is the single source of truth; adding a permission produces a reviewed InsertData migration.
        builder.HasData(PermissionCatalog.All.Select(static definition => new PermissionRow { Code = definition.Code }));
    }
}
