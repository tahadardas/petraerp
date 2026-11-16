using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.GeneralLedger;

internal sealed class AccountAuditStateConfiguration : IEntityTypeConfiguration<AccountAuditStateRow>
{
    public void Configure(EntityTypeBuilder<AccountAuditStateRow> builder)
    {
        builder.ToTable("AccountAuditStates", "ledger", table =>
        {
            table.HasCheckConstraint("CK_AccountAuditStates_Stage", "[Stage] IN (1, 2)");
            table.HasCheckConstraint("CK_AccountAuditStates_Code", StorageConventions.NormalizedRequired("[Code]"));
            table.HasCheckConstraint("CK_AccountAuditStates_Names", StorageConventions.NormalizedNames);
            table.HasCheckConstraint("CK_AccountAuditStates_Types", "[AccountType] BETWEEN 1 AND 5 AND [CostCenterPolicy] BETWEEN 1 AND 3 AND [CurrencyMode] BETWEEN 1 AND 3");
            table.HasCheckConstraint("CK_AccountAuditStates_Currency", "([CurrencyMode] = 3 AND [FixedCurrencyCode] IS NOT NULL) OR ([CurrencyMode] <> 3 AND [FixedCurrencyCode] IS NULL)");
        });
        builder.HasKey(row => new { row.EventId, row.Stage });
        builder.Property(row => row.EventId).ValueGeneratedNever();
        builder.Property(row => row.Stage).HasColumnType("tinyint");
        builder.Property(row => row.AccountType).HasColumnType("tinyint");
        builder.Property(row => row.CostCenterPolicy).HasColumnType("tinyint");
        builder.Property(row => row.CurrencyMode).HasColumnType("tinyint");
        builder.Property(row => row.Code).HasMaxLength(64).UseCollation(StorageConventions.OrdinalCollation);
        builder.Property(row => row.NameArabic).HasMaxLength(200);
        builder.Property(row => row.NameEnglish).HasMaxLength(200);
        builder.Property(row => row.FixedCurrencyCode).HasColumnType("char(3)").UseCollation(StorageConventions.OrdinalCollation);
        builder.HasOne<AccountAuditRow>().WithMany().HasForeignKey(row => row.EventId).OnDelete(DeleteBehavior.Restrict);
    }
}
