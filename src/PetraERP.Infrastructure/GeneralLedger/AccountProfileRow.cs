using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.GeneralLedger;

/// <summary>Al-Ameen account-card attributes; one optional row per account (no row = all defaults).</summary>
internal sealed class AccountProfileRow : ITrackedStorageRow
{
    public Guid AccountId { get; set; }
    public Guid CompanyId { get; set; }
    public string? Notes { get; set; }
    public int Nature { get; set; } = 3;
    public Guid? ClosingAccountId { get; set; }
    public Guid? DefaultCostCenterId { get; set; }
    public int BudgetKind { get; set; }
    public decimal? BudgetAmount { get; set; }
    public string? BudgetCurrencyCode { get; set; }
    public decimal? BudgetRate { get; set; }
    public bool IncludeCommercialPapers { get; set; }
    public string? IncomeStatementCategory { get; set; }
    public string? EquityCategory { get; set; }
    public string? FinancialPositionCategory { get; set; }
    public string? CashFlowCategory { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

internal sealed class AccountProfileConfiguration : IEntityTypeConfiguration<AccountProfileRow>
{
    public void Configure(EntityTypeBuilder<AccountProfileRow> builder)
    {
        builder.ToTable("AccountProfiles", "ledger", table =>
        {
            table.HasCheckConstraint("CK_AccountProfiles_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_AccountProfiles_Nature", "[Nature] IN (1, 2, 3)");
            table.HasCheckConstraint("CK_AccountProfiles_NotSelfClosing", "[ClosingAccountId] IS NULL OR [ClosingAccountId] <> [AccountId]");
            table.HasCheckConstraint("CK_AccountProfiles_Budget",
                "([BudgetKind] = 0 AND [BudgetAmount] IS NULL AND [BudgetCurrencyCode] IS NULL AND [BudgetRate] IS NULL)"
                + " OR ([BudgetKind] IN (1, 2) AND [BudgetAmount] >= 0 AND [BudgetCurrencyCode] IS NOT NULL AND [BudgetRate] > 0)");
            StorageConventions.UtcCheck(table, "CK_AccountProfiles_Utc");
        });

        builder.HasKey(r => r.AccountId);
        builder.Property(r => r.AccountId).ValueGeneratedNever();
        builder.Property(r => r.CompanyId).IsRequired();
        builder.Property(r => r.Notes).HasMaxLength(500);
        builder.Property(r => r.BudgetAmount).HasPrecision(19, 4);
        builder.Property(r => r.BudgetCurrencyCode).HasMaxLength(3);
        builder.Property(r => r.BudgetRate).HasPrecision(19, 8);
        builder.Property(r => r.IncomeStatementCategory).HasMaxLength(100);
        builder.Property(r => r.EquityCategory).HasMaxLength(100);
        builder.Property(r => r.FinancialPositionCategory).HasMaxLength(100);
        builder.Property(r => r.CashFlowCategory).HasMaxLength(100);
        StorageConventions.Tracking(builder);

        builder.HasIndex(r => r.CompanyId).HasDatabaseName("IX_AccountProfiles_Company");
        builder.HasOne<CompanyRow>().WithMany().HasForeignKey(r => r.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AccountRow>().WithOne().HasForeignKey<AccountProfileRow>(r => r.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AccountRow>().WithMany().HasForeignKey(r => r.ClosingAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
