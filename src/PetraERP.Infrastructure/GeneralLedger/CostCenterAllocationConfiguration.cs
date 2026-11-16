using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.GeneralLedger;

internal sealed class CostCenterAllocationConfiguration : IEntityTypeConfiguration<CostCenterAllocationRow>
{
    public void Configure(EntityTypeBuilder<CostCenterAllocationRow> builder)
    {
        builder.ToTable("CostCenterAllocations", "ledger", table =>
        {
            table.HasCheckConstraint("CK_CostCenterAllocations_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_CostCenterAllocations_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_CostCenterAllocations_SourceTarget", "[SourceCostCenterId] <> [TargetCostCenterId]");
            table.HasCheckConstraint("CK_CostCenterAllocations_Percentage", "[Percentage] > 0 AND [Percentage] <= 100");
            StorageConventions.UtcCheck(table, "CK_CostCenterAllocations_Utc");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.CompanyId).IsRequired();
        builder.Property(r => r.SourceCostCenterId).IsRequired();
        builder.Property(r => r.TargetCostCenterId).IsRequired();
        builder.Property(r => r.Percentage).HasPrecision(7, 4).IsRequired();

        StorageConventions.Tracking(builder);

        builder.HasIndex(r => new { r.CompanyId, r.SourceCostCenterId, r.TargetCostCenterId })
               .IsUnique()
               .HasDatabaseName("UQ_CostCenterAllocations_Source_Target");

        builder.HasOne<CompanyRow>()
               .WithMany()
               .HasForeignKey(r => r.CompanyId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.SourceCostCenter)
               .WithMany()
               .HasForeignKey(r => r.SourceCostCenterId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.TargetCostCenter)
               .WithMany()
               .HasForeignKey(r => r.TargetCostCenterId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
