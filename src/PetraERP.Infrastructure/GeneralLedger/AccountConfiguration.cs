using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Currencies;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.GeneralLedger;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<AccountRow>
{
    public void Configure(EntityTypeBuilder<AccountRow> builder)
    {
        builder.ToTable("Accounts", "ledger", table =>
        {
            table.HasCheckConstraint("CK_Accounts_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_Accounts_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_Accounts_Code", StorageConventions.NormalizedRequired("[Code]"));
            table.HasCheckConstraint("CK_Accounts_Names", StorageConventions.NormalizedNames);
            table.HasCheckConstraint("CK_Accounts_AccountType", "[AccountType] IN (1, 2, 3, 4, 5)");
            table.HasCheckConstraint("CK_Accounts_CostCenterPolicy", "[CostCenterPolicy] IN (1, 2, 3)");
            table.HasCheckConstraint("CK_Accounts_CurrencyMode", "[CurrencyMode] IN (1, 2, 3)");
            table.HasCheckConstraint("CK_Accounts_FxMonetaryEligibility", "[FxMonetaryEligibility] IN (1, 2, 3, 4)");
            // FixedCurrencyCode must be present iff mode=3 (FixedCurrency)
            table.HasCheckConstraint("CK_Accounts_FixedCurrency",
                "([CurrencyMode] = 3 AND [FixedCurrencyCode] IS NOT NULL) OR ([CurrencyMode] <> 3 AND [FixedCurrencyCode] IS NULL)");
            // Self-FK: parent must be in the same company (enforced via FK + app logic)
            table.HasCheckConstraint("CK_Accounts_ParentNotSelf",
                "[ParentId] IS NULL OR [ParentId] <> [Id]");
            StorageConventions.UtcCheck(table, "CK_Accounts_Utc");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.CompanyId).IsRequired();
        builder.Property(r => r.Code).HasMaxLength(64).UseCollation(StorageConventions.OrdinalCollation).IsRequired();
        builder.Property(r => r.NameArabic).HasMaxLength(200).IsRequired();
        builder.Property(r => r.NameEnglish).HasMaxLength(200);
        builder.Property(r => r.AccountType).HasColumnType("tinyint").IsRequired();
        builder.Property(r => r.PostingAllowed).IsRequired();
        builder.Property(r => r.CostCenterPolicy).HasColumnType("tinyint").IsRequired();
        builder.Property(r => r.CurrencyMode).HasColumnType("tinyint").IsRequired();
        builder.Property(r => r.FxMonetaryEligibility).HasColumnType("tinyint").HasDefaultValue((byte)1).IsRequired();
        builder.Property(r => r.FixedCurrencyCode)
               .HasColumnType("char(3)")
               .UseCollation(StorageConventions.OrdinalCollation);
        builder.Property(r => r.ParentId);
        builder.Property(r => r.IsActive).IsRequired();

        StorageConventions.Tracking(builder);

        // Unique code per company
        builder.HasIndex(r => new { r.CompanyId, r.Code })
               .IsUnique()
               .HasDatabaseName("UQ_Accounts_CompanyCode");

        // Lookup index for posting validation
        builder.HasIndex(r => new { r.CompanyId, r.IsActive, r.PostingAllowed })
               .HasDatabaseName("IX_Accounts_CompanyActivePosting");

        builder.HasOne<CompanyRow>()
               .WithMany()
               .HasForeignKey(r => r.CompanyId)
               .HasConstraintName("FK_Accounts_Companies")
               .OnDelete(DeleteBehavior.Restrict);

        // Parent self-reference: same table
        builder.HasOne<AccountRow>()
               .WithMany()
               .HasForeignKey(r => r.ParentId)
               .HasConstraintName("FK_Accounts_ParentAccount")
               .OnDelete(DeleteBehavior.Restrict)
               .IsRequired(false);

        // FixedCurrencyCode FK — restrict to prevent orphaned fixed-currency accounts
        builder.HasOne<CurrencyRow>()
               .WithMany()
               .HasForeignKey(r => r.FixedCurrencyCode)
               .HasConstraintName("FK_Accounts_FixedCurrency")
               .OnDelete(DeleteBehavior.Restrict)
               .IsRequired(false);
    }
}
