using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Security;

namespace PetraERP.Infrastructure.Patterns;

internal sealed class PatternRevisionConfiguration : IEntityTypeConfiguration<PatternRevisionRow>
{
    public void Configure(EntityTypeBuilder<PatternRevisionRow> builder)
    {
        builder.ToTable("PatternRevisions", "patterns", table =>
        {
            table.HasCheckConstraint("CK_PatternRevisions_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_PatternRevisions_PatternId", "[PatternId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_PatternRevisions_RevisionNumber", "[RevisionNumber] > 0");
            table.HasCheckConstraint("CK_PatternRevisions_CreatedByUserId", "[CreatedByUserId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_PatternRevisions_Reason", StorageConventions.NormalizedRequired("[Reason]"));
            table.HasCheckConstraint("CK_PatternRevisions_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.PatternId).IsRequired();
        builder.Property(r => r.RevisionNumber).IsRequired();
        builder.Property(r => r.State).IsRequired();
        builder.Property(r => r.EffectiveFromUtc).HasPrecision(7);
        builder.Property(r => r.EffectiveToUtc).HasPrecision(7);
        builder.Property(r => r.ConfigurationJson).IsRequired();
        builder.Property(r => r.CreatedByUserId).IsRequired();
        builder.Property(r => r.Reason).HasMaxLength(500).IsRequired();
        builder.Property(r => r.CreatedAtUtc).HasPrecision(7).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(r => new { r.PatternId, r.RevisionNumber })
               .IsUnique()
               .HasDatabaseName("UQ_PatternRevisions_PatternRevisionNumber");

        builder.HasOne<UserRow>()
               .WithMany()
               .HasForeignKey(r => r.CreatedByUserId)
               .HasConstraintName("FK_PatternRevisions_Users")
               .OnDelete(DeleteBehavior.Restrict);
    }
}
