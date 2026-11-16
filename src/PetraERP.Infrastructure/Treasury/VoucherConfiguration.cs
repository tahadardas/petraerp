using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Parties;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Treasury;

internal sealed class VoucherConfiguration : IEntityTypeConfiguration<VoucherRow>, IEntityTypeConfiguration<VoucherAllocationRow>,
    IEntityTypeConfiguration<VoucherLineRow>
{
    public void Configure(EntityTypeBuilder<VoucherRow> builder)
    {
        builder.ToTable("Vouchers", "treasury", table =>
        {
            table.HasCheckConstraint("CK_Vouchers_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_Vouchers_Kind", "[Kind] IN (1, 2)");
            table.HasCheckConstraint("CK_Vouchers_Status", "[Status] IN (0, 1, 2, 3)");
            table.HasCheckConstraint("CK_Vouchers_SettlementMethod", "[SettlementMethod] IN (1, 2)");
            table.HasCheckConstraint("CK_Vouchers_Amount", "[Amount] > 0");
            table.HasCheckConstraint("CK_Vouchers_ExchangeRate", "[ExchangeRate] > 0");
            table.HasCheckConstraint("CK_Vouchers_ReceiptParty", "[Kind] <> 1 OR [PartyId] IS NOT NULL");
            table.HasCheckConstraint("CK_Vouchers_Posted",
                "[Status] IN (0,1) OR ([JournalEntryId] IS NOT NULL AND [VoucherNumber] IS NOT NULL AND [PostingKey] IS NOT NULL AND [PostedAtUtc] IS NOT NULL)");
            table.HasCheckConstraint("CK_Vouchers_Reversed",
                "([Status] = 3 AND [ReversalJournalEntryId] IS NOT NULL AND [ReversedAtUtc] IS NOT NULL AND [ReversalReason] IS NOT NULL) OR ([Status] <> 3 AND [ReversalJournalEntryId] IS NULL AND [ReversedAtUtc] IS NULL AND [ReversalReason] IS NULL)");
            table.HasCheckConstraint("CK_Vouchers_VoucherDateUtc", "DATEPART(TZOFFSET, [VoucherDateUtc]) = 0");
            StorageConventions.UtcCheck(table, "CK_Vouchers_Utc");
        });
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.VoucherNumber).HasMaxLength(64).UseCollation(StorageConventions.OrdinalCollation);
        builder.Property(r => r.VoucherDateUtc).HasPrecision(7);
        builder.Property(r => r.CurrencyCode).HasMaxLength(3).IsFixedLength().IsUnicode(false);
        builder.Property(r => r.ExchangeRate).HasPrecision(19, 8);
        builder.Property(r => r.Amount).HasPrecision(19, 4);
        builder.Property(r => r.Notes).HasMaxLength(1000);
        builder.Property(r => r.SourceNumber).HasMaxLength(64);
        builder.Property(r => r.SourceDate).HasColumnType("date");
        builder.Property(r => r.PostingKey).HasMaxLength(128).UseCollation(StorageConventions.OrdinalCollation);
        builder.Property(r => r.ReversalReason).HasMaxLength(500);
        builder.Property(r => r.PostedAtUtc).HasPrecision(7);
        builder.Property(r => r.ReversedAtUtc).HasPrecision(7);
        builder.Property(r => r.ApprovedAtUtc).HasPrecision(7); builder.Property(r => r.ApprovalReason).HasMaxLength(500);
        builder.HasOne<AccountRow>().WithMany().HasForeignKey(r => r.PartyControlAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Security.UserRow>().WithMany().HasForeignKey(r => r.ApprovedBy).OnDelete(DeleteBehavior.Restrict);
        StorageConventions.Tracking(builder);

        builder.HasIndex(r => new { r.CompanyId, r.BranchId, r.VoucherNumber }).IsUnique()
            .HasFilter("[VoucherNumber] IS NOT NULL").HasDatabaseName("UQ_Vouchers_Number");
        builder.HasIndex(r => new { r.CompanyId, r.BranchId, r.PostingKey }).IsUnique()
            .HasFilter("[PostingKey] IS NOT NULL").HasDatabaseName("UQ_Vouchers_PostingKey");
        builder.HasIndex(r => r.JournalEntryId).IsUnique().HasFilter("[JournalEntryId] IS NOT NULL");
        builder.HasIndex(r => r.ReversalJournalEntryId).IsUnique().HasFilter("[ReversalJournalEntryId] IS NOT NULL");
        builder.HasIndex(r => new { r.CompanyId, r.BranchId, r.Kind, r.VoucherDateUtc }).HasDatabaseName("IX_Vouchers_Scope_Kind_Date");
        builder.HasIndex(r => new { r.CompanyId, r.PartyId }).HasDatabaseName("IX_Vouchers_Company_Party");

        builder.HasOne<CompanyRow>().WithMany().HasForeignKey(r => r.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BranchRow>().WithMany().HasForeignKey(r => r.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.PatternRevision).WithMany().HasForeignKey(r => r.PatternRevisionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.Party).WithMany().HasForeignKey(r => r.PartyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<JournalEntryRow>().WithMany().HasForeignKey(r => r.JournalEntryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<JournalEntryRow>().WithMany().HasForeignKey(r => r.ReversalJournalEntryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(r => r.Allocations).WithOne(a => a.Voucher).HasForeignKey(a => a.VoucherId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(r => r.Lines).WithOne(l => l.Voucher).HasForeignKey(l => l.VoucherId).OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<VoucherAllocationRow> builder)
    {
        builder.ToTable("VoucherAllocations", "treasury", table =>
        {
            table.HasCheckConstraint("CK_VoucherAllocations_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_VoucherAllocations_Amount", "[Amount] > 0");
        });
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Amount).HasPrecision(19, 4);
        builder.HasIndex(r => new { r.VoucherId, r.SalesInvoiceId }).IsUnique().HasDatabaseName("UQ_VoucherAllocations_Invoice");
        builder.HasIndex(r => r.SalesInvoiceId).HasDatabaseName("IX_VoucherAllocations_SalesInvoiceId");
        builder.HasOne(r => r.Invoice).WithMany().HasForeignKey(r => r.SalesInvoiceId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<VoucherLineRow> builder)
    {
        builder.ToTable("VoucherLines", "treasury", table =>
        {
            table.HasCheckConstraint("CK_VoucherLines_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_VoucherLines_Amount", "[Amount] > 0");
            table.HasCheckConstraint("CK_VoucherLines_Target", "([PartyId] IS NULL AND [AccountId] IS NOT NULL) OR ([PartyId] IS NOT NULL AND [AccountId] IS NULL)");
        });
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Amount).HasPrecision(19, 4);
        builder.Property(r => r.Description).HasMaxLength(500);
        builder.HasIndex(r => r.VoucherId).HasDatabaseName("IX_VoucherLines_VoucherId");
        builder.HasOne<PartyRow>().WithMany().HasForeignKey(r => r.PartyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AccountRow>().WithMany().HasForeignKey(r => r.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CostCenterRow>().WithMany().HasForeignKey(r => r.CostCenterId).OnDelete(DeleteBehavior.Restrict);
    }
}
