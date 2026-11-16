using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Patterns;

internal sealed class DocumentPatternConfiguration : IEntityTypeConfiguration<DocumentPatternRow>
{
    public void Configure(EntityTypeBuilder<DocumentPatternRow> builder)
    {
        builder.ToTable("DocumentPatterns", "patterns", table =>
        {
            table.HasCheckConstraint("CK_DocumentPatterns_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_DocumentPatterns_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_DocumentPatterns_Code", StorageConventions.NormalizedRequired("[Code]"));
            table.HasCheckConstraint("CK_DocumentPatterns_Names", StorageConventions.NormalizedNames);
            table.HasCheckConstraint("CK_DocumentPatterns_ShortCode", StorageConventions.NormalizedRequired("[ShortCode]"));
            StorageConventions.UtcCheck(table, "CK_DocumentPatterns_Utc");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.CompanyId).IsRequired();
        builder.Property(r => r.OperationKind).IsRequired();
        builder.Property(r => r.Code).HasMaxLength(64).UseCollation(StorageConventions.OrdinalCollation).IsRequired();
        builder.Property(r => r.NameArabic).HasMaxLength(200).IsRequired();
        builder.Property(r => r.NameEnglish).HasMaxLength(200);
        builder.Property(r => r.ShortCode).HasMaxLength(16).UseCollation(StorageConventions.OrdinalCollation).IsRequired();
        builder.Property(r => r.Icon).HasMaxLength(64);
        builder.Property(r => r.Group).HasMaxLength(64);
        builder.Property(r => r.IsActive).IsRequired();
        builder.Property(r => r.IsDefault).IsRequired();
        builder.Property(r => r.ActiveRevisionId);

        StorageConventions.Tracking(builder);

        builder.HasIndex(r => new { r.CompanyId, r.OperationKind, r.Code })
               .IsUnique()
               .HasDatabaseName("UQ_DocumentPatterns_CompanyOperationCode");

        builder.HasIndex(r => new { r.CompanyId, r.IsActive })
               .HasDatabaseName("IX_DocumentPatterns_CompanyActive");

        builder.HasOne<CompanyRow>()
               .WithMany()
               .HasForeignKey(r => r.CompanyId)
               .HasConstraintName("FK_DocumentPatterns_Companies")
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(r => r.Revisions)
               .WithOne(r => r.Pattern)
               .HasForeignKey(r => r.PatternId)
               .HasConstraintName("FK_PatternRevisions_DocumentPatterns")
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(r => r.BranchGrants)
               .WithOne(r => r.Pattern)
               .HasForeignKey(r => r.PatternId)
               .HasConstraintName("FK_PatternBranchGrants_DocumentPatterns")
               .OnDelete(DeleteBehavior.Cascade);
    }
}
