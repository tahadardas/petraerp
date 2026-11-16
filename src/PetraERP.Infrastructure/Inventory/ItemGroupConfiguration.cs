using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Inventory;

internal sealed class ItemGroupConfiguration : IEntityTypeConfiguration<ItemGroupRow>
{
    public void Configure(EntityTypeBuilder<ItemGroupRow> builder)
    {
        builder.ToTable("ItemGroups", "inventory", table =>
        {
            table.HasCheckConstraint("CK_ItemGroups_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_ItemGroups_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_ItemGroups_Code", StorageConventions.NormalizedRequired("[Code]"));
            table.HasCheckConstraint("CK_ItemGroups_Names", StorageConventions.NormalizedNames);
            table.HasCheckConstraint("CK_ItemGroups_ParentSelf", "[ParentId] IS NULL OR [ParentId] <> [Id]");
            StorageConventions.UtcCheck(table, "CK_ItemGroups_Utc");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.CompanyId).IsRequired();
        builder.Property(r => r.ParentId);
        builder.Property(r => r.Code).HasMaxLength(64).UseCollation(StorageConventions.OrdinalCollation).IsRequired();
        builder.Property(r => r.NameArabic).HasMaxLength(200).IsRequired();
        builder.Property(r => r.NameEnglish).HasMaxLength(200);
        builder.Property(r => r.Description).HasMaxLength(1000);
        builder.Property(r => r.IsActive).IsRequired();

        StorageConventions.Tracking(builder);

        builder.HasIndex(r => new { r.CompanyId, r.Code })
               .IsUnique()
               .HasDatabaseName("UQ_ItemGroups_CompanyCode");

        builder.HasIndex(r => new { r.CompanyId, r.ParentId })
               .HasDatabaseName("IX_ItemGroups_CompanyParent");

        builder.HasOne<CompanyRow>()
               .WithMany()
               .HasForeignKey(r => r.CompanyId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Parent)
               .WithMany(r => r.Children)
               .HasForeignKey(r => r.ParentId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
