using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.GeneralLedger;

internal sealed class CostCenterConfiguration : IEntityTypeConfiguration<CostCenterRow>
{
    public void Configure(EntityTypeBuilder<CostCenterRow> builder)
    {
        builder.ToTable("CostCenters", "ledger", table =>
        {
            table.HasCheckConstraint("CK_CostCenters_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_CostCenters_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_CostCenters_Code", StorageConventions.NormalizedRequired("[Code]"));
            table.HasCheckConstraint("CK_CostCenters_Names", StorageConventions.NormalizedNames);
            table.HasCheckConstraint("CK_CostCenters_CenterType", "[CenterType] IN (1, 2, 3, 4)");
            table.HasCheckConstraint("CK_CostCenters_ParentSelf", "[ParentId] IS NULL OR [ParentId] <> [Id]");
            StorageConventions.UtcCheck(table, "CK_CostCenters_Utc");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.CompanyId).IsRequired();
        builder.Property(r => r.ParentId);
        builder.Property(r => r.CenterType).IsRequired().HasDefaultValue((byte)1);
        builder.Property(r => r.Code).HasMaxLength(64).UseCollation(StorageConventions.OrdinalCollation).IsRequired();
        builder.Property(r => r.NameArabic).HasMaxLength(200).IsRequired();
        builder.Property(r => r.NameEnglish).HasMaxLength(200);
        builder.Property(r => r.Description).HasMaxLength(1000);
        builder.Property(r => r.StartDate);
        builder.Property(r => r.EndDate);
        builder.Property(r => r.ResponsiblePerson).HasMaxLength(200);
        builder.Property(r => r.IsActive).IsRequired();

        StorageConventions.Tracking(builder);

        builder.HasIndex(r => new { r.CompanyId, r.Code })
               .IsUnique()
               .HasDatabaseName("UQ_CostCenters_CompanyCode");

        builder.HasIndex(r => new { r.CompanyId, r.ParentId })
               .HasDatabaseName("IX_CostCenters_CompanyParent");

        builder.HasOne<CompanyRow>()
               .WithMany()
               .HasForeignKey(r => r.CompanyId)
               .HasConstraintName("FK_CostCenters_Companies")
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Parent)
               .WithMany(r => r.Children)
               .HasForeignKey(r => r.ParentId)
               .HasConstraintName("FK_CostCenters_Parent")
               .OnDelete(DeleteBehavior.Restrict);
    }
}
