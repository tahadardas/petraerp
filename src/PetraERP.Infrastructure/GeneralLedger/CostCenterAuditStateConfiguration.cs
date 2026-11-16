using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.GeneralLedger;

internal sealed class CostCenterAuditStateConfiguration : IEntityTypeConfiguration<CostCenterAuditStateRow>
{
    public void Configure(EntityTypeBuilder<CostCenterAuditStateRow> builder)
    {
        builder.ToTable("CostCenterAuditStates", "ledger", table =>
        {
            table.HasCheckConstraint("CK_CostCenterAuditStates_Stage", "[Stage] IN (1,2)");
            table.HasCheckConstraint("CK_CostCenterAuditStates_Code", StorageConventions.NormalizedRequired("[Code]"));
            table.HasCheckConstraint("CK_CostCenterAuditStates_Names", StorageConventions.NormalizedNames);
        });
        builder.HasKey(row => new { row.EventId, row.Stage });
        builder.Property(row => row.EventId).ValueGeneratedNever();
        builder.Property(row => row.Stage).HasColumnType("tinyint");
        builder.Property(row => row.Code).HasMaxLength(64).UseCollation(StorageConventions.OrdinalCollation);
        builder.Property(row => row.NameArabic).HasMaxLength(200);
        builder.Property(row => row.NameEnglish).HasMaxLength(200);
        builder.HasOne<CostCenterAuditRow>().WithMany().HasForeignKey(row => row.EventId).OnDelete(DeleteBehavior.Restrict);
    }
}
