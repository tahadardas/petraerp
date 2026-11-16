using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetraERP.Infrastructure.Persistence.AccountingMigrations;

/// <summary>Permits the domain's approved purchase state without requiring premature financial effects.</summary>
public partial class PurchaseApprovalStates : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(name: "CK_PurchaseInvoices_Status", schema: "purchases", table: "Invoices");
        migrationBuilder.DropCheckConstraint(name: "CK_PurchaseInvoices_Posted", schema: "purchases", table: "Invoices");
        migrationBuilder.AddCheckConstraint(name: "CK_PurchaseInvoices_Status", schema: "purchases", table: "Invoices",
            sql: "[Status] IN (0, 1, 2, 3)");
        migrationBuilder.AddCheckConstraint(name: "CK_PurchaseInvoices_Posted", schema: "purchases", table: "Invoices",
            sql: "[Status] IN (0, 1) OR ([JournalEntryId] IS NOT NULL AND [InvoiceNumber] IS NOT NULL AND [PostingKey] IS NOT NULL AND [PostedAtUtc] IS NOT NULL)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Older schemas cannot represent these valid domain states. Never delete, rewrite or silently downgrade them.
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [purchases].[Invoices] WHERE [Status] IN (1, 3))
                THROW 51042, N'Purchase approval downgrade is blocked: approved or reversed invoices require this schema.', 1;
            """);
        migrationBuilder.DropCheckConstraint(name: "CK_PurchaseInvoices_Status", schema: "purchases", table: "Invoices");
        migrationBuilder.DropCheckConstraint(name: "CK_PurchaseInvoices_Posted", schema: "purchases", table: "Invoices");
        migrationBuilder.AddCheckConstraint(name: "CK_PurchaseInvoices_Status", schema: "purchases", table: "Invoices",
            sql: "[Status] IN (0, 2)");
        migrationBuilder.AddCheckConstraint(name: "CK_PurchaseInvoices_Posted", schema: "purchases", table: "Invoices",
            sql: "[Status] = 0 OR ([JournalEntryId] IS NOT NULL AND [InvoiceNumber] IS NOT NULL AND [PostingKey] IS NOT NULL AND [PostedAtUtc] IS NOT NULL)");
    }
}
