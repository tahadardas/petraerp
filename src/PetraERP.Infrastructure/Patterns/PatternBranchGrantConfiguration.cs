using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Patterns;

internal sealed class PatternBranchGrantConfiguration : IEntityTypeConfiguration<PatternBranchGrantRow>
{
    public void Configure(EntityTypeBuilder<PatternBranchGrantRow> builder)
    {
        builder.ToTable("PatternBranchGrants", "patterns", table =>
        {
            table.HasCheckConstraint("CK_PatternBranchGrants_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_PatternBranchGrants_PatternId", "[PatternId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_PatternBranchGrants_BranchId", "[BranchId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_PatternBranchGrants_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.PatternId).IsRequired();
        builder.Property(r => r.BranchId).IsRequired();
        builder.Property(r => r.CreatedAtUtc).HasPrecision(7).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(r => new { r.PatternId, r.BranchId })
               .IsUnique()
               .HasDatabaseName("UQ_PatternBranchGrants_PatternBranch");

        builder.HasOne<BranchRow>()
               .WithMany()
               .HasForeignKey(r => r.BranchId)
               .HasConstraintName("FK_PatternBranchGrants_Branches")
               .OnDelete(DeleteBehavior.Restrict);
    }
}
