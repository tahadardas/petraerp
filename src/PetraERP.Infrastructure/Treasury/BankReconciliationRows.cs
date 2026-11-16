using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.AccountingCalendars;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Security;

namespace PetraERP.Infrastructure.Treasury;

internal sealed class BankReconciliationRow : ITrackedStorageRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid FiscalYearId { get; set; }
    public Guid BranchId { get; set; }
    public Guid AccountId { get; set; }
    public DateOnly StatementDate { get; set; }
    public decimal StatementBalance { get; set; }
    public string Status { get; set; } = "Draft";
    public Guid CreatedByUserId { get; set; }
    public Guid? CompletedByUserId { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

internal sealed class BankReconciliationLineRow
{
    public Guid ReconciliationId { get; set; }
    public Guid JournalLineId { get; set; }
    /// <summary>Signed base-currency amount at the time of clearing (debit positive); fixed so later reads cannot drift.</summary>
    public decimal Amount { get; set; }
}

internal sealed class BankReconciliationConfiguration : IEntityTypeConfiguration<BankReconciliationRow>
{
    public void Configure(EntityTypeBuilder<BankReconciliationRow> b)
    {
        b.ToTable("BankReconciliations", "treasury", t =>
        {
            t.HasCheckConstraint("CK_BankReconciliations_Id", StorageConventions.NonemptyId);
            t.HasCheckConstraint("CK_BankReconciliations_Status", "[Status] IN (N'Draft',N'Completed')");
            t.HasCheckConstraint("CK_BankReconciliations_Completion",
                "([Status]=N'Completed' AND [CompletedAtUtc] IS NOT NULL AND [CompletedByUserId] IS NOT NULL) OR ([Status]=N'Draft' AND [CompletedAtUtc] IS NULL AND [CompletedByUserId] IS NULL)");
            StorageConventions.UtcCheck(t, "CK_BankReconciliations_Utc");
        });
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).ValueGeneratedNever();
        b.Property(r => r.StatementBalance).HasPrecision(19, 4);
        b.Property(r => r.Status).HasMaxLength(16).IsRequired();
        b.Property(r => r.CompletedAtUtc).HasPrecision(7);
        StorageConventions.Tracking(b);
        // One open reconciliation per book account and branch; completed ones are history.
        b.HasIndex(r => new { r.CompanyId, r.BranchId, r.AccountId }).IsUnique().HasFilter("[Status] = N'Draft'");
        b.HasIndex(r => new { r.CompanyId, r.BranchId, r.AccountId, r.StatementDate });
        b.HasOne<CompanyRow>().WithMany().HasForeignKey(r => r.CompanyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<BranchRow>().WithMany().HasForeignKey(r => r.BranchId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<FiscalYearRow>().WithMany().HasForeignKey(r => r.FiscalYearId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AccountRow>().WithMany().HasForeignKey(r => r.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserRow>().WithMany().HasForeignKey(r => r.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserRow>().WithMany().HasForeignKey(r => r.CompletedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BankReconciliationLineConfiguration : IEntityTypeConfiguration<BankReconciliationLineRow>
{
    public void Configure(EntityTypeBuilder<BankReconciliationLineRow> b)
    {
        b.ToTable("BankReconciliationLines", "treasury");
        b.HasKey(r => new { r.ReconciliationId, r.JournalLineId });
        b.Property(r => r.Amount).HasPrecision(19, 4);
        // A journal line is cleared at most once, ever (and while a draft holds it, no other draft can).
        b.HasIndex(r => r.JournalLineId).IsUnique();
        b.HasOne<BankReconciliationRow>().WithMany().HasForeignKey(r => r.ReconciliationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<JournalLineRow>().WithMany().HasForeignKey(r => r.JournalLineId).OnDelete(DeleteBehavior.Restrict);
    }
}
