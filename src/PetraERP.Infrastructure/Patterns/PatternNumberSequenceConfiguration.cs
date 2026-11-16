using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Patterns;

internal sealed class PatternNumberSequenceConfiguration : IEntityTypeConfiguration<PatternNumberSequenceRow>
{
    public void Configure(EntityTypeBuilder<PatternNumberSequenceRow> builder)
    {
        builder.ToTable("PatternNumberSequences", "patterns", table =>
        {
            table.HasCheckConstraint("CK_PatternNumberSequences_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_PatternNumberSequences_PatternId", "[PatternId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_PatternNumberSequences_LastSequence", "[LastSequence] >= 0");
            StorageConventions.UtcCheck(table, "CK_PatternNumberSequences_Utc");
        });

        builder.HasKey(r => new { r.CompanyId, r.BranchId, r.PatternId, r.FiscalYearId });
        builder.Property(r => r.CompanyId).IsRequired();
        builder.Property(r => r.BranchId).IsRequired();
        builder.Property(r => r.PatternId).IsRequired();
        builder.Property(r => r.FiscalYearId).IsRequired();
        builder.Property(r => r.LastSequence).IsRequired();

        StorageConventions.Tracking(builder);

        builder.HasOne<CompanyRow>()
               .WithMany()
               .HasForeignKey(r => r.CompanyId)
               .HasConstraintName("FK_PatternNumberSequences_Companies")
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<DocumentPatternRow>()
               .WithMany()
               .HasForeignKey(r => r.PatternId)
               .HasConstraintName("FK_PatternNumberSequences_DocumentPatterns")
               .OnDelete(DeleteBehavior.Cascade);
    }
}
