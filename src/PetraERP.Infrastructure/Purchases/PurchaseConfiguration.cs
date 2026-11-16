using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Purchases;

internal sealed class PurchaseConfiguration : IEntityTypeConfiguration<PurchaseInvoiceRow>, IEntityTypeConfiguration<PurchaseInvoiceLineRow>, IEntityTypeConfiguration<PurchaseInvoiceAdjustmentRow>
{
    public void Configure(EntityTypeBuilder<PurchaseInvoiceRow> builder)
    {
        builder.HasOne<AccountRow>().WithMany().HasForeignKey(r => r.VarianceAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable("Invoices", "purchases", table =>
        {
            table.HasCheckConstraint("CK_PurchaseInvoices_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_PurchaseInvoices_Status", "[Status] IN (0, 1, 2, 3)");
            table.HasCheckConstraint("CK_PurchaseInvoices_PaymentMethod", "[PaymentMethod] IN (1, 2)");
            table.HasCheckConstraint("CK_PurchaseInvoices_SellerCode", "[SellerCode] IS NULL OR [SellerCode] >= 0");
            table.HasCheckConstraint("CK_PurchaseInvoices_DistributorCode", "[DistributorCode] IS NULL OR [DistributorCode] >= 0");
            table.HasCheckConstraint("CK_PurchaseInvoices_ExchangeRate", "[ExchangeRate] > 0");
            table.HasCheckConstraint("CK_PurchaseInvoices_Totals",
                "[SubTotal] >= 0 AND [DiscountTotal] >= 0 AND [TaxTotal] >= 0 AND [AdditionsTotal] >= 0 AND [GrandTotal] = [SubTotal] - [DiscountTotal] + [TaxTotal] + [AdditionsTotal]");
            table.HasCheckConstraint("CK_PurchaseInvoices_Posted",
                "[Status] IN (0, 1) OR ([JournalEntryId] IS NOT NULL AND [InvoiceNumber] IS NOT NULL AND [PostingKey] IS NOT NULL AND [PostedAtUtc] IS NOT NULL)");
            table.HasCheckConstraint("CK_PurchaseInvoices_Return", "[ReturnOfInvoiceId] IS NULL OR ([Status] = 2 AND [ReturnOfInvoiceId] <> [Id])");
            // A document is an original, a reversal or a return — never two kinds — and a reversal is stored already posted.
            table.HasCheckConstraint("CK_PurchaseInvoices_ReversalKind",
                "[ReversesInvoiceId] IS NULL OR ([ReturnOfInvoiceId] IS NULL AND [ReversedByInvoiceId] IS NULL AND [ReversesInvoiceId] <> [Id] "
                + "AND [Status] = 2 AND [JournalEntryId] IS NOT NULL AND [InvoiceNumber] IS NOT NULL)");
            table.HasCheckConstraint("CK_PurchaseInvoices_ReversedLink", "[ReversedByInvoiceId] IS NULL OR [Status] = 3");
            table.HasCheckConstraint("CK_PurchaseInvoices_InvoiceDateUtc", "DATEPART(TZOFFSET, [InvoiceDateUtc]) = 0");
            StorageConventions.UtcCheck(table, "CK_PurchaseInvoices_Utc");
        });
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.InvoiceNumber).HasMaxLength(64).UseCollation(StorageConventions.OrdinalCollation);
        builder.Property(r => r.SupplierReference).HasMaxLength(64);
        builder.Property(r => r.InvoiceDateUtc).HasPrecision(7);
        builder.Property(r => r.CurrencyCode).HasMaxLength(3).IsFixedLength().IsUnicode(false);
        builder.Property(r => r.ExchangeRate).HasPrecision(19, 8);
        builder.Property(r => r.Notes).HasMaxLength(1000);
        builder.Property(r => r.SubTotal).HasPrecision(19, 4);
        builder.Property(r => r.DiscountTotal).HasPrecision(19, 4);
        builder.Property(r => r.TaxTotal).HasPrecision(19, 4);
        builder.Property(r => r.AdditionsTotal).HasPrecision(19, 4).HasDefaultValue(0m);
        builder.Property(r => r.GrandTotal).HasPrecision(19, 4);
        builder.Property(r => r.PostingKey).HasMaxLength(128).UseCollation(StorageConventions.OrdinalCollation);
        builder.Property(r => r.PostedAtUtc).HasPrecision(7);
        StorageConventions.Tracking(builder);

        builder.HasIndex(r => new { r.CompanyId, r.BranchId, r.InvoiceNumber }).IsUnique()
            .HasFilter("[InvoiceNumber] IS NOT NULL").HasDatabaseName("UQ_PurchaseInvoices_Number");
        builder.HasIndex(r => new { r.CompanyId, r.BranchId, r.PostingKey }).IsUnique()
            .HasFilter("[PostingKey] IS NOT NULL").HasDatabaseName("UQ_PurchaseInvoices_PostingKey");
        builder.HasIndex(r => r.JournalEntryId).IsUnique().HasFilter("[JournalEntryId] IS NOT NULL");
        builder.HasIndex(r => r.ReturnOfInvoiceId).HasFilter("[ReturnOfInvoiceId] IS NOT NULL").HasDatabaseName("IX_PurchaseInvoices_ReturnOf");
        builder.HasIndex(r => r.ReversesInvoiceId).IsUnique().HasFilter("[ReversesInvoiceId] IS NOT NULL").HasDatabaseName("IX_PurchaseInvoices_Reverses");
        builder.HasIndex(r => new { r.CompanyId, r.SupplierId }).HasDatabaseName("IX_PurchaseInvoices_Company_Supplier");
        builder.HasIndex(r => new { r.CompanyId, r.BranchId, r.InvoiceDateUtc }).HasDatabaseName("IX_PurchaseInvoices_Scope_Date");

        builder.HasOne<CompanyRow>().WithMany().HasForeignKey(r => r.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BranchRow>().WithMany().HasForeignKey(r => r.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.PatternRevision).WithMany().HasForeignKey(r => r.PatternRevisionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.Supplier).WithMany().HasForeignKey(r => r.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.Warehouse).WithMany().HasForeignKey(r => r.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<JournalEntryRow>().WithMany().HasForeignKey(r => r.JournalEntryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PurchaseInvoiceRow>().WithMany().HasForeignKey(r => r.ReturnOfInvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PurchaseInvoiceRow>().WithMany().HasForeignKey(r => r.ReversesInvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PurchaseInvoiceRow>().WithMany().HasForeignKey(r => r.ReversedByInvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(r => r.Lines).WithOne(l => l.Invoice).HasForeignKey(l => l.PurchaseInvoiceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(r => r.Adjustments).WithOne(a => a.Invoice).HasForeignKey(a => a.PurchaseInvoiceId).OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<PurchaseInvoiceAdjustmentRow> builder)
    {
        builder.ToTable("InvoiceAdjustments", "purchases", table =>
        {
            table.HasCheckConstraint("CK_PurchaseInvoiceAdjustments_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_PurchaseInvoiceAdjustments_Kind", "[Kind] IN (1, 2)");
            table.HasCheckConstraint("CK_PurchaseInvoiceAdjustments_Amount", "[Amount] > 0");
            table.HasCheckConstraint("CK_PurchaseInvoiceAdjustments_Percentage", "[Percentage] >= 0 AND [Percentage] <= 100");
        });
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Amount).HasPrecision(19, 4);
        builder.Property(r => r.Percentage).HasPrecision(19, 4);
        builder.Property(r => r.Notes).HasMaxLength(500);
        builder.HasIndex(r => r.PurchaseInvoiceId).HasDatabaseName("IX_PurchaseInvoiceAdjustments_InvoiceId");
        builder.HasOne(r => r.Account).WithMany().HasForeignKey(r => r.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AccountRow>().WithMany().HasForeignKey(r => r.CounterAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CostCenterRow>().WithMany().HasForeignKey(r => r.CostCenterId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<PurchaseInvoiceLineRow> builder)
    {
        builder.ToTable("InvoiceLines", "purchases", table =>
        {
            table.HasCheckConstraint("CK_PurchaseInvoiceLines_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_PurchaseInvoiceLines_Quantity", "[Quantity] > 0");
            table.HasCheckConstraint("CK_PurchaseInvoiceLines_Money", "[UnitCost] >= 0 AND [DiscountAmount] >= 0 AND [TaxAmount] >= 0 AND [LineTotal] >= 0");
            table.HasCheckConstraint("CK_PurchaseInvoiceLines_SourceLineId", "[SourceLineId] IS NULL OR [SourceLineId] <> [Id]");
        });
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Quantity).HasPrecision(19, 4);
        builder.Property(r => r.UnitCost).HasPrecision(19, 4);
        builder.Property(r => r.DiscountAmount).HasPrecision(19, 4);
        builder.Property(r => r.TaxAmount).HasPrecision(19, 4);
        builder.Property(r => r.LineTotal).HasPrecision(19, 4);
        builder.Property(r => r.Notes).HasMaxLength(500);
        builder.HasIndex(r => r.PurchaseInvoiceId).HasDatabaseName("IX_PurchaseInvoiceLines_InvoiceId");
        builder.HasIndex(r => r.SourceLineId).HasFilter("[SourceLineId] IS NOT NULL").HasDatabaseName("IX_PurchaseInvoiceLines_SourceLineId");
        builder.HasOne(r => r.Item).WithMany().HasForeignKey(r => r.ItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.Unit).WithMany().HasForeignKey(r => r.UnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CostCenterRow>().WithMany().HasForeignKey(r => r.CostCenterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PurchaseInvoiceLineRow>().WithMany().HasForeignKey(r => r.SourceLineId).OnDelete(DeleteBehavior.Restrict);
    }
}
