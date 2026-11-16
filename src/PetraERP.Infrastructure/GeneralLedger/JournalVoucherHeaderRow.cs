using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.GeneralLedger;

/// <summary>
/// Marks a journal as an Al-Ameen cash-book voucher (سند يومية) and names its cash account. A journal keeps no line order,
/// so the cash account cannot be recovered from the lines alone; this row is the only addition to the journal.
/// </summary>
internal sealed class JournalVoucherHeaderRow : ITrackedStorageRow
{
    public const string Daily = "DAILY";

    public Guid JournalId { get; set; }
    public Guid CompanyId { get; set; }
    public string Kind { get; set; } = Daily;
    public Guid CashAccountId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

internal sealed class JournalVoucherHeaderConfiguration : IEntityTypeConfiguration<JournalVoucherHeaderRow>
{
    public void Configure(EntityTypeBuilder<JournalVoucherHeaderRow> builder)
    {
        builder.ToTable("JournalVoucherHeaders", "ledger", table =>
        {
            table.HasCheckConstraint("CK_JournalVoucherHeaders_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_JournalVoucherHeaders_Kind", "[Kind] IN (N'DAILY')");
            StorageConventions.UtcCheck(table, "CK_JournalVoucherHeaders_Utc");
        });

        builder.HasKey(r => r.JournalId);
        builder.Property(r => r.JournalId).ValueGeneratedNever();
        builder.Property(r => r.CompanyId).IsRequired();
        builder.Property(r => r.Kind).HasMaxLength(20).IsRequired();
        StorageConventions.Tracking(builder);

        builder.HasIndex(r => new { r.CompanyId, r.Kind }).HasDatabaseName("IX_JournalVoucherHeaders_CompanyKind");
        builder.HasIndex(r => r.CashAccountId).HasDatabaseName("IX_JournalVoucherHeaders_CashAccount");
        builder.HasOne<CompanyRow>().WithMany().HasForeignKey(r => r.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<JournalEntryRow>().WithOne().HasForeignKey<JournalVoucherHeaderRow>(r => r.JournalId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AccountRow>().WithMany().HasForeignKey(r => r.CashAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
