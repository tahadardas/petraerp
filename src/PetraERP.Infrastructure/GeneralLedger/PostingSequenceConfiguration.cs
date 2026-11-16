using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.AccountingCalendars;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.GeneralLedger;

internal sealed class PostingSequenceConfiguration : IEntityTypeConfiguration<PostingSequenceRow>
{
    public void Configure(EntityTypeBuilder<PostingSequenceRow> builder)
    {
        builder.ToTable("PostingSequences", "ledger", table =>
        {
            table.HasCheckConstraint("CK_PostingSequences_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_PostingSequences_BranchId", "[BranchId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_PostingSequences_FiscalYearId", "[FiscalYearId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_PostingSequences_LastSequence", "[LastSequence] >= 0");
            StorageConventions.UtcCheck(table, "CK_PostingSequences_Utc");
        });

        // Composite PK: one counter per company+branch+year
        builder.HasKey(r => new { r.CompanyId, r.BranchId, r.FiscalYearId });
        builder.Property(r => r.CompanyId).IsRequired();
        builder.Property(r => r.BranchId).IsRequired();
        builder.Property(r => r.FiscalYearId).IsRequired();
        builder.Property(r => r.LastSequence).IsRequired();

        StorageConventions.Tracking(builder);

        builder.HasOne<CompanyRow>()
               .WithMany()
               .HasForeignKey(r => r.CompanyId)
               .HasConstraintName("FK_PostingSequences_Companies")
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<BranchRow>()
               .WithMany()
               .HasForeignKey(r => r.BranchId)
               .HasConstraintName("FK_PostingSequences_Branches")
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<FiscalYearRow>()
               .WithMany()
               .HasForeignKey(r => r.FiscalYearId)
               .HasConstraintName("FK_PostingSequences_FiscalYears")
               .OnDelete(DeleteBehavior.Restrict);
    }
}
