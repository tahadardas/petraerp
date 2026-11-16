using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Inventory;
using PetraERP.Infrastructure.Organizations;
using PetraERP.Infrastructure.Parties;
using PetraERP.Infrastructure.Patterns;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Sales;

internal sealed class SalesInvoiceConfiguration : IEntityTypeConfiguration<SalesInvoiceRow>, IEntityTypeConfiguration<SalesInvoiceLineRow>, IEntityTypeConfiguration<SalesInvoiceAdjustmentRow>
{
    public void Configure(EntityTypeBuilder<SalesInvoiceRow> builder)
    {
        builder.ToTable("Invoices", "sales", table =>
        {
            table.HasCheckConstraint("CK_SalesInvoices_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_SalesInvoices_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_SalesInvoices_BranchId", "[BranchId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_SalesInvoices_CustomerId", "[CustomerId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_SalesInvoices_WarehouseId", "[WarehouseId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_SalesInvoices_Status", "[Status] IN (0, 1, 2, 3, 4)");
            table.HasCheckConstraint("CK_SalesInvoices_PaymentMethod", "[PaymentMethod] IN (1, 2)");
            table.HasCheckConstraint("CK_SalesInvoices_SellerCode", "[SellerCode] IS NULL OR [SellerCode] >= 0");
            table.HasCheckConstraint("CK_SalesInvoices_DistributorCode", "[DistributorCode] IS NULL OR [DistributorCode] >= 0");
            table.HasCheckConstraint("CK_SalesInvoices_ExchangeRate", "[ExchangeRate] > 0");
            table.HasCheckConstraint("CK_SalesInvoices_InvoiceDateUtc", "DATEPART(TZOFFSET, [InvoiceDateUtc]) = 0");
            // A document is an original, a full reversal or a partial return; never two kinds at once. Returns are only stored posted.
            table.HasCheckConstraint("CK_SalesInvoices_ReturnKind",
                "[ReturnOfInvoiceId] IS NULL OR ([ReversesInvoiceId] IS NULL AND [ReversedByInvoiceId] IS NULL AND [Status] = 2 AND [JournalEntryId] IS NOT NULL AND [ReturnOfInvoiceId] <> [Id])");
            StorageConventions.UtcCheck(table, "CK_SalesInvoices_Utc");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.CompanyId).IsRequired();
        builder.Property(r => r.BranchId).IsRequired();
        builder.Property(r => r.PatternRevisionId).IsRequired();
        builder.Property(r => r.CustomerId).IsRequired();
        builder.Property(r => r.WarehouseId).IsRequired();
        builder.Property(r => r.InvoiceNumber).HasMaxLength(64).UseCollation(StorageConventions.OrdinalCollation);
        builder.Property(r => r.InvoiceDateUtc).HasPrecision(7).IsRequired();
        builder.Property(r => r.PaymentMethod).IsRequired();
        builder.Property(r => r.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(r => r.ExchangeRate).HasPrecision(19, 8).IsRequired();
        builder.Property(r => r.Status).IsRequired();
        builder.Property(r => r.CostCenterId);
        builder.Property(r => r.Notes).HasMaxLength(1000);

        builder.Property(r => r.SubTotal).HasPrecision(19, 4).IsRequired();
        builder.Property(r => r.DiscountTotal).HasPrecision(19, 4).IsRequired();
        builder.Property(r => r.TaxTotal).HasPrecision(19, 4).IsRequired();
        builder.Property(r => r.AdditionsTotal).HasPrecision(19, 4).IsRequired();
        builder.Property(r => r.GrandTotal).HasPrecision(19, 4).IsRequired();

        builder.Property(r => r.JournalEntryId);
        builder.Property(r => r.ReversedByInvoiceId);
        builder.Property(r => r.ReversesInvoiceId);
        builder.HasOne<JournalEntryRow>().WithMany().HasForeignKey(r => r.JournalEntryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AccountRow>().WithMany().HasForeignKey(r => r.CustomerAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SalesInvoiceRow>().WithMany().HasForeignKey(r => r.ReversesInvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SalesInvoiceRow>().WithMany().HasForeignKey(r => r.ReversedByInvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => r.JournalEntryId).IsUnique().HasFilter("[JournalEntryId] IS NOT NULL");
        builder.HasIndex(r => r.ReversesInvoiceId).IsUnique().HasFilter("[ReversesInvoiceId] IS NOT NULL");
        builder.HasOne<SalesInvoiceRow>().WithMany().HasForeignKey(r => r.ReturnOfInvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => r.ReturnOfInvoiceId).HasFilter("[ReturnOfInvoiceId] IS NOT NULL").HasDatabaseName("IX_SalesInvoices_ReturnOf");

        builder.Property(r => r.CreatedByUserId);
        builder.Property(r => r.ApprovedByUserId);
        builder.Property(r => r.ApprovedAtUtc).HasPrecision(7);
        builder.Property(r => r.PostedByUserId);
        builder.Property(r => r.PostedAtUtc).HasPrecision(7);

        StorageConventions.Tracking(builder);

        builder.HasIndex(r => new { r.CompanyId, r.BranchId, r.InvoiceNumber })
               .IsUnique()
               .HasFilter("[InvoiceNumber] IS NOT NULL")
               .HasDatabaseName("UQ_SalesInvoices_Number");

        builder.HasIndex(r => new { r.CompanyId, r.CustomerId })
               .HasDatabaseName("IX_SalesInvoices_Company_Customer");

        builder.HasIndex(r => new { r.CompanyId, r.Status })
               .HasDatabaseName("IX_SalesInvoices_Company_Status");

        builder.HasIndex(r => new { r.CompanyId, r.InvoiceDateUtc })
               .HasDatabaseName("IX_SalesInvoices_Company_Date");

        builder.HasOne<CompanyRow>()
               .WithMany()
               .HasForeignKey(r => r.CompanyId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<BranchRow>()
               .WithMany()
               .HasForeignKey(r => r.BranchId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.PatternRevision)
               .WithMany()
               .HasForeignKey(r => r.PatternRevisionId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Customer)
               .WithMany()
               .HasForeignKey(r => r.CustomerId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Warehouse)
               .WithMany()
               .HasForeignKey(r => r.WarehouseId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<CostCenterRow>()
               .WithMany()
               .HasForeignKey(r => r.CostCenterId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(r => r.Lines)
               .WithOne(l => l.Invoice)
               .HasForeignKey(l => l.SalesInvoiceId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(r => r.Adjustments)
               .WithOne(a => a.Invoice)
               .HasForeignKey(a => a.SalesInvoiceId)
               .OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<SalesInvoiceAdjustmentRow> builder)
    {
        builder.ToTable("InvoiceAdjustments", "sales", table =>
        {
            table.HasCheckConstraint("CK_SalesInvoiceAdjustments_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_SalesInvoiceAdjustments_Kind", "[Kind] IN (1, 2)");
            table.HasCheckConstraint("CK_SalesInvoiceAdjustments_Amount", "[Amount] > 0");
            table.HasCheckConstraint("CK_SalesInvoiceAdjustments_Percentage", "[Percentage] >= 0 AND [Percentage] <= 100");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.SalesInvoiceId).IsRequired();
        builder.Property(r => r.Kind).IsRequired();
        builder.Property(r => r.AccountId).IsRequired();
        builder.Property(r => r.Amount).HasPrecision(19, 4).IsRequired();
        builder.Property(r => r.Percentage).HasPrecision(19, 4).IsRequired();
        builder.Property(r => r.Notes).HasMaxLength(500);
        builder.Property(r => r.DisplayOrder).IsRequired();

        builder.HasIndex(r => r.SalesInvoiceId).HasDatabaseName("IX_SalesInvoiceAdjustments_InvoiceId");

        builder.HasOne(r => r.Account)
               .WithMany()
               .HasForeignKey(r => r.AccountId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AccountRow>()
               .WithMany()
               .HasForeignKey(r => r.CounterAccountId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<CostCenterRow>()
               .WithMany()
               .HasForeignKey(r => r.CostCenterId)
               .OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<SalesInvoiceLineRow> builder)
    {
        builder.ToTable("InvoiceLines", "sales", table =>
        {
            table.HasCheckConstraint("CK_SalesInvoiceLines_Id", StorageConventions.NonemptyId);
            table.HasCheckConstraint("CK_SalesInvoiceLines_Quantity", "[Quantity] > 0");
            table.HasCheckConstraint("CK_SalesInvoiceLines_UnitPrice", "[UnitPrice] >= 0");
            table.HasCheckConstraint("CK_SalesInvoiceLines_DiscountPercentage", "[DiscountPercentage] >= 0 AND [DiscountPercentage] <= 100");
            table.HasCheckConstraint("CK_SalesInvoiceLines_DiscountAmount", "[DiscountAmount] >= 0");
            table.HasCheckConstraint("CK_SalesInvoiceLines_TaxPercentage", "[TaxPercentage] >= 0 AND [TaxPercentage] <= 100");
            table.HasCheckConstraint("CK_SalesInvoiceLines_TaxAmount", "[TaxAmount] >= 0");
            table.HasCheckConstraint("CK_SalesInvoiceLines_PumpCost", "[PumpCost] >= 0");
            table.HasCheckConstraint("CK_SalesInvoiceLines_TransportCost", "[TransportCost] >= 0");
            table.HasCheckConstraint("CK_SalesInvoiceLines_SourceLineId", "[SourceLineId] IS NULL OR [SourceLineId] <> [Id]");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.SalesInvoiceId).IsRequired();
        builder.Property(r => r.ItemId).IsRequired();
        builder.Property(r => r.UnitId).IsRequired();
        builder.Property(r => r.Quantity).HasPrecision(19, 4).IsRequired();
        builder.Property(r => r.UnitPrice).HasPrecision(19, 4).IsRequired();
        builder.Property(r => r.DiscountPercentage).HasPrecision(19, 4).IsRequired();
        builder.Property(r => r.DiscountAmount).HasPrecision(19, 4).IsRequired();
        builder.Property(r => r.TaxPercentage).HasPrecision(19, 4).IsRequired();
        builder.Property(r => r.TaxAmount).HasPrecision(19, 4).IsRequired();
        builder.Property(r => r.PumpCost).HasPrecision(19, 4).IsRequired();
        builder.Property(r => r.TransportCost).HasPrecision(19, 4).IsRequired();
        builder.Property(r => r.LineTotal).HasPrecision(19, 4).IsRequired();
        builder.Property(r => r.CostCenterId);
        builder.Property(r => r.WarehouseId);
        builder.Property(r => r.Notes).HasMaxLength(500);
        builder.Property(r => r.DisplayOrder).IsRequired();
        builder.Property(r => r.ConcreteGrade).HasMaxLength(50);

        builder.HasIndex(r => r.SalesInvoiceId)
               .HasDatabaseName("IX_SalesInvoiceLines_InvoiceId");

        builder.HasOne<SalesInvoiceLineRow>()
               .WithMany()
               .HasForeignKey(r => r.SourceLineId)
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => r.SourceLineId)
               .HasFilter("[SourceLineId] IS NOT NULL")
               .HasDatabaseName("IX_SalesInvoiceLines_SourceLineId");

        builder.HasOne(r => r.Item)
               .WithMany()
               .HasForeignKey(r => r.ItemId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Unit)
               .WithMany()
               .HasForeignKey(r => r.UnitId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<CostCenterRow>()
               .WithMany()
               .HasForeignKey(r => r.CostCenterId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<WarehouseRow>()
               .WithMany()
               .HasForeignKey(r => r.WarehouseId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
