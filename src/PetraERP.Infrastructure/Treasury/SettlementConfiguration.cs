using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Parties.OpenItems;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Purchases;
using PetraERP.Infrastructure.Sales;
using PetraERP.Infrastructure.Security;

namespace PetraERP.Infrastructure.Treasury;

internal sealed class SettlementConfiguration : IEntityTypeConfiguration<VoucherAllocationPlanRow>, IEntityTypeConfiguration<SettlementBatchRow>, IEntityTypeConfiguration<SettlementApplicationRow>
{
    public void Configure(EntityTypeBuilder<VoucherAllocationPlanRow> b)
    {
        b.ToTable("AllocationPlans", "treasury", t =>
        {
            t.HasCheckConstraint("CK_AllocationPlans_Target",
                "([SalesInvoiceId] IS NOT NULL AND [PurchaseInvoiceId] IS NULL AND [OpenItemId] IS NULL) OR ([SalesInvoiceId] IS NULL AND [PurchaseInvoiceId] IS NOT NULL AND [OpenItemId] IS NULL) OR ([SalesInvoiceId] IS NULL AND [PurchaseInvoiceId] IS NULL AND [OpenItemId] IS NOT NULL)");
            t.HasCheckConstraint("CK_AllocationPlans_Amounts", "[InvoiceAmount]>0 AND [SettlementAmount]>0 AND [CrossRate]>0");
        });
        b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.InvoiceAmount).HasPrecision(19,4); b.Property(x => x.SettlementAmount).HasPrecision(19,4); b.Property(x => x.CrossRate).HasPrecision(19,8);
        b.HasOne(x => x.Voucher).WithMany(x => x.AllocationPlans).HasForeignKey(x => x.VoucherId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<SalesInvoiceRow>().WithMany().HasForeignKey(x => x.SalesInvoiceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PurchaseInvoiceRow>().WithMany().HasForeignKey(x => x.PurchaseInvoiceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<OpenItemRow>().WithMany().HasForeignKey(x => x.OpenItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.VoucherId, x.SalesInvoiceId }).IsUnique().HasFilter("[SalesInvoiceId] IS NOT NULL");
        b.HasIndex(x => new { x.VoucherId, x.PurchaseInvoiceId }).IsUnique().HasFilter("[PurchaseInvoiceId] IS NOT NULL");
        b.HasIndex(x => new { x.VoucherId, x.OpenItemId }).IsUnique().HasFilter("[OpenItemId] IS NOT NULL");
    }
    public void Configure(EntityTypeBuilder<SettlementBatchRow> b)
    {
        b.ToTable("SettlementBatches", "treasury", t =>
        {
            t.HasCheckConstraint("CK_SettlementBatches_State", "[State] IN (1,2)");
            t.HasCheckConstraint("CK_SettlementBatches_Reason", "LEN(LTRIM(RTRIM([Reason])))>0");
            t.HasCheckConstraint("CK_SettlementBatches_Reversal", "[State]=1 OR ([ReversedAtUtc] IS NOT NULL AND [ReversedBy] IS NOT NULL AND [ReversalReason] IS NOT NULL)");
            t.HasCheckConstraint("CK_SettlementBatches_Utc", "DATEPART(TZOFFSET,[DateUtc])=0 AND DATEPART(TZOFFSET,[RecordedAtUtc])=0");
        });
        b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.Property(x => x.RequestKey).HasMaxLength(128).UseCollation(StorageConventions.OrdinalCollation);
        b.Property(x => x.Fingerprint).HasMaxLength(64); b.Property(x => x.Reason).HasMaxLength(500); b.Property(x => x.ReversalReason).HasMaxLength(500);
        b.Property(x => x.DateUtc).HasPrecision(7); b.Property(x => x.RecordedAtUtc).HasPrecision(7); b.Property(x => x.ReversedAtUtc).HasPrecision(7);
        b.HasIndex(x => new { x.CompanyId, x.BranchId, x.RequestKey }).IsUnique();
        b.HasIndex(x => new { x.VoucherId, x.DateUtc });
        b.HasOne(x => x.Voucher).WithMany().HasForeignKey(x => x.VoucherId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CompanyRow>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<BranchRow>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserRow>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserRow>().WithMany().HasForeignKey(x => x.ReversedBy).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<JournalEntryRow>().WithMany().HasForeignKey(x => x.JournalId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<JournalEntryRow>().WithMany().HasForeignKey(x => x.ReversalJournalId).OnDelete(DeleteBehavior.Restrict);
    }
    public void Configure(EntityTypeBuilder<SettlementApplicationRow> b)
    {
        b.ToTable("SettlementApplications", "treasury", t =>
        {
            t.HasCheckConstraint("CK_SettlementApplications_Target",
                "([SalesInvoiceId] IS NOT NULL AND [PurchaseInvoiceId] IS NULL AND [OpenItemId] IS NULL) OR ([SalesInvoiceId] IS NULL AND [PurchaseInvoiceId] IS NOT NULL AND [OpenItemId] IS NULL) OR ([SalesInvoiceId] IS NULL AND [PurchaseInvoiceId] IS NULL AND [OpenItemId] IS NOT NULL)");
            t.HasCheckConstraint("CK_SettlementApplications_Amounts", "[InvoiceAmount]>0 AND [SettlementAmount]>0 AND [BookedBaseAmount]>=0 AND [SettlementBaseAmount]>=0 AND [InvoiceRate]>0 AND [SettlementRate]>0 AND [CrossRate]>0");
            t.HasTrigger("TR_SettlementApplications_Immutable"); t.UseSqlOutputClause(false);
        });
        b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.InvoiceCurrency).HasMaxLength(3); b.Property(x => x.SettlementCurrency).HasMaxLength(3);
        b.Property(x => x.InvoiceAmount).HasPrecision(19,4); b.Property(x => x.SettlementAmount).HasPrecision(19,4);
        b.Property(x => x.BookedBaseAmount).HasPrecision(19,4); b.Property(x => x.SettlementBaseAmount).HasPrecision(19,4);
        b.Property(x => x.CrossRate).HasPrecision(19,8); b.Property(x => x.InvoiceRate).HasPrecision(19,8); b.Property(x => x.SettlementRate).HasPrecision(19,8);
        b.HasOne(x => x.Batch).WithMany(x => x.Applications).HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Voucher).WithMany().HasForeignKey(x => x.VoucherId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SalesInvoiceRow>().WithMany().HasForeignKey(x => x.SalesInvoiceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PurchaseInvoiceRow>().WithMany().HasForeignKey(x => x.PurchaseInvoiceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<OpenItemRow>().WithMany().HasForeignKey(x => x.OpenItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AccountRow>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.BatchId, x.SalesInvoiceId }).IsUnique().HasFilter("[SalesInvoiceId] IS NOT NULL");
        b.HasIndex(x => new { x.BatchId, x.PurchaseInvoiceId }).IsUnique().HasFilter("[PurchaseInvoiceId] IS NOT NULL");
        b.HasIndex(x => new { x.BatchId, x.OpenItemId }).IsUnique().HasFilter("[OpenItemId] IS NOT NULL");
    }
}
