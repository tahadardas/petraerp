using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Inventory;

internal sealed class WarehouseConfiguration : IEntityTypeConfiguration<WarehouseRow>
{
    public void Configure(EntityTypeBuilder<WarehouseRow> builder)
    {
        builder.ToTable("Warehouses", "inventory", table =>
        {
            table.HasCheckConstraint("CK_Warehouses_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_Warehouses_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_Warehouses_BranchId", "[BranchId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_Warehouses_Code", StorageConventions.NormalizedRequired("[Code]"));
            table.HasCheckConstraint("CK_Warehouses_Names", StorageConventions.NormalizedNames);
            StorageConventions.UtcCheck(table, "CK_Warehouses_Utc");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.CompanyId).IsRequired();
        builder.Property(r => r.BranchId).IsRequired();
        builder.Property(r => r.Code).HasMaxLength(64).UseCollation(StorageConventions.OrdinalCollation).IsRequired();
        builder.Property(r => r.NameArabic).HasMaxLength(200).IsRequired();
        builder.Property(r => r.NameEnglish).HasMaxLength(200);
        builder.Property(r => r.Location).HasMaxLength(200);
        builder.Property(r => r.IsActive).IsRequired();

        StorageConventions.Tracking(builder);

        builder.HasIndex(r => new { r.CompanyId, r.Code })
               .IsUnique()
               .HasDatabaseName("UQ_Warehouses_CompanyCode");

        builder.HasIndex(r => new { r.CompanyId, r.BranchId, r.IsActive })
               .HasDatabaseName("IX_Warehouses_CompanyBranchActive");

        builder.HasOne<CompanyRow>()
               .WithMany()
               .HasForeignKey(r => r.CompanyId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<BranchRow>()
               .WithMany()
               .HasForeignKey(r => new { r.CompanyId, r.BranchId })
               .HasPrincipalKey(r => new { r.CompanyId, r.Id })
               .OnDelete(DeleteBehavior.Restrict);
    }
}
