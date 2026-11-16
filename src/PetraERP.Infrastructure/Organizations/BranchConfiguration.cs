using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Organizations;

internal sealed class BranchConfiguration : IEntityTypeConfiguration<BranchRow>
{
    public void Configure(EntityTypeBuilder<BranchRow> builder)
    {
        builder.ToTable("Branches", "system", table =>
        {
            table.HasCheckConstraint("CK_Branches_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_Branches_Code", StorageConventions.NormalizedRequired("[Code]"));
            table.HasCheckConstraint("CK_Branches_Names", StorageConventions.NormalizedNames);
            table.HasCheckConstraint("CK_Branches_ParentNotSelf", "[ParentBranchId] IS NULL OR [ParentBranchId] <> [Id]");
            table.HasCheckConstraint("CK_Branches_NumberPrefix", "[NumberPrefix] IS NULL OR ([NumberPrefix] NOT LIKE '%[^A-Z0-9]%' AND DATALENGTH([NumberPrefix]) BETWEEN 1 AND 8)");
            StorageConventions.UtcCheck(table, "CK_Branches_Utc");
        });
        builder.HasKey(static row => row.Id);
        builder.Property(static row => row.Id).ValueGeneratedNever();
        builder.HasAlternateKey(static row => new { row.CompanyId, row.Id });
        builder.Property(static row => row.Code).HasMaxLength(64).UseCollation(StorageConventions.OrdinalCollation).IsRequired();
        builder.HasIndex(static row => new { row.CompanyId, row.Code }).IsUnique();
        builder.Property(static row => row.NameArabic).HasMaxLength(200).IsRequired();
        builder.Property(static row => row.NameEnglish).HasMaxLength(200);
        builder.Property(static row => row.NumberPrefix).HasMaxLength(8).UseCollation(StorageConventions.OrdinalCollation);
        builder.Property(static row => row.Address).HasMaxLength(400);
        builder.Property(static row => row.Phone).HasMaxLength(50);
        builder.Property(static row => row.Email).HasMaxLength(200);
        builder.Property(static row => row.Notes).HasMaxLength(1000);
        builder.HasIndex(static row => new { row.CompanyId, row.NumberPrefix }).IsUnique().HasFilter("[NumberPrefix] IS NOT NULL");
        builder.HasOne<BranchRow>().WithMany().HasForeignKey(static row => new { row.CompanyId, row.ParentBranchId })
            .HasPrincipalKey(static row => new { row.CompanyId, row.Id }).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<CompanyRow>().WithMany().HasForeignKey(static row => row.CompanyId).OnDelete(DeleteBehavior.NoAction);
        StorageConventions.Tracking(builder);
    }
}
