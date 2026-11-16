using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Security;

internal sealed class RoleConfiguration : IEntityTypeConfiguration<RoleRow>
{
    public void Configure(EntityTypeBuilder<RoleRow> builder)
    {
        builder.ToTable("Roles", SecurityStorage.Schema, table =>
        {
            table.HasCheckConstraint("CK_Roles_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_Roles_Code",
                $"LEN([Code]) BETWEEN 2 AND 64 AND {SecurityStorage.AsciiIdentifier("[Code]", "-A-Z0-9._")}");
            table.HasCheckConstraint("CK_Roles_Names", StorageConventions.NormalizedNames);
            StorageConventions.UtcCheck(table, "CK_Roles_Utc");
        });
        builder.HasKey(static row => row.Id);
        builder.Property(static row => row.Id).ValueGeneratedNever();
        builder.Property(static row => row.Code).HasMaxLength(64).UseCollation(StorageConventions.OrdinalCollation).IsRequired();
        builder.HasIndex(static row => row.Code).IsUnique();
        builder.Property(static row => row.NameArabic).HasMaxLength(200).IsRequired();
        builder.Property(static row => row.NameEnglish).HasMaxLength(200);
        StorageConventions.Tracking(builder);
    }
}
