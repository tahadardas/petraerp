using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Inventory;

internal sealed class ItemAccountMappingConfiguration : IEntityTypeConfiguration<ItemAccountMappingRow>
{
    public void Configure(EntityTypeBuilder<ItemAccountMappingRow> builder)
    {
        builder.ToTable("ItemAccountMappings", "inventory", table =>
        {
            table.HasCheckConstraint("CK_ItemAccountMappings_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_ItemAccountMappings_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_ItemAccountMappings_OwnerId", "[OwnerId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_ItemAccountMappings_OwnerType", "[OwnerType] IN (1, 2)");
            table.HasCheckConstraint("CK_ItemAccountMappings_Role", StorageConventions.NormalizedRequired("[Role]"));
            StorageConventions.UtcCheck(table, "CK_ItemAccountMappings_Utc");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.CompanyId).IsRequired();
        builder.Property(r => r.OwnerType).IsRequired();
        builder.Property(r => r.OwnerId).IsRequired();
        builder.Property(r => r.Role).HasMaxLength(64).UseCollation(StorageConventions.OrdinalCollation).IsRequired();
        builder.Property(r => r.AccountId).IsRequired();
        builder.Property(r => r.PatternId);
        builder.Property(r => r.IsActive).IsRequired();

        StorageConventions.Tracking(builder);

        builder.HasIndex(r => new { r.CompanyId, r.OwnerType, r.OwnerId, r.Role, r.PatternId })
               .IsUnique()
               .HasDatabaseName("UQ_ItemAccountMappings_Scope");

        builder.HasIndex(r => new { r.CompanyId, r.AccountId })
               .HasDatabaseName("IX_ItemAccountMappings_Account");

        builder.HasOne<CompanyRow>()
               .WithMany()
               .HasForeignKey(r => r.CompanyId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Account)
               .WithMany()
               .HasForeignKey(r => r.AccountId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
