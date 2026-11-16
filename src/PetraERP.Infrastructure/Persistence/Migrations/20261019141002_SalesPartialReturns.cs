using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetraERP.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class SalesPartialReturns : Migration
{
    private static readonly string[] DocumentLine = ["CompanyId", "DocumentType", "DocumentLineId"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "DocumentLineId",
            schema: "inventory",
            table: "StockMovements",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "ReturnOfInvoiceId",
            schema: "sales",
            table: "Invoices",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "SourceLineId",
            schema: "sales",
            table: "InvoiceLines",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "UQ_StockMovements_DocumentLine",
            schema: "inventory",
            table: "StockMovements",
            columns: DocumentLine,
            unique: true,
            filter: "[DocumentLineId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_SalesInvoices_ReturnOf",
            schema: "sales",
            table: "Invoices",
            column: "ReturnOfInvoiceId",
            filter: "[ReturnOfInvoiceId] IS NOT NULL");

        migrationBuilder.AddCheckConstraint(
            name: "CK_SalesInvoices_ReturnKind",
            schema: "sales",
            table: "Invoices",
            sql: "[ReturnOfInvoiceId] IS NULL OR ([ReversesInvoiceId] IS NULL AND [ReversedByInvoiceId] IS NULL AND [Status] = 2 AND [JournalEntryId] IS NOT NULL AND [ReturnOfInvoiceId] <> [Id])");

        migrationBuilder.CreateIndex(
            name: "IX_SalesInvoiceLines_SourceLineId",
            schema: "sales",
            table: "InvoiceLines",
            column: "SourceLineId",
            filter: "[SourceLineId] IS NOT NULL");

        migrationBuilder.AddCheckConstraint(
            name: "CK_SalesInvoiceLines_SourceLineId",
            schema: "sales",
            table: "InvoiceLines",
            sql: "[SourceLineId] IS NULL OR [SourceLineId] <> [Id]");

        migrationBuilder.AddForeignKey(
            name: "FK_InvoiceLines_InvoiceLines_SourceLineId",
            schema: "sales",
            table: "InvoiceLines",
            column: "SourceLineId",
            principalSchema: "sales",
            principalTable: "InvoiceLines",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_Invoices_Invoices_ReturnOfInvoiceId",
            schema: "sales",
            table: "Invoices",
            column: "ReturnOfInvoiceId",
            principalSchema: "sales",
            principalTable: "Invoices",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_InvoiceLines_InvoiceLines_SourceLineId",
            schema: "sales",
            table: "InvoiceLines");

        migrationBuilder.DropForeignKey(
            name: "FK_Invoices_Invoices_ReturnOfInvoiceId",
            schema: "sales",
            table: "Invoices");

        migrationBuilder.DropIndex(
            name: "UQ_StockMovements_DocumentLine",
            schema: "inventory",
            table: "StockMovements");

        migrationBuilder.DropIndex(
            name: "IX_SalesInvoices_ReturnOf",
            schema: "sales",
            table: "Invoices");

        migrationBuilder.DropCheckConstraint(
            name: "CK_SalesInvoices_ReturnKind",
            schema: "sales",
            table: "Invoices");

        migrationBuilder.DropIndex(
            name: "IX_SalesInvoiceLines_SourceLineId",
            schema: "sales",
            table: "InvoiceLines");

        migrationBuilder.DropCheckConstraint(
            name: "CK_SalesInvoiceLines_SourceLineId",
            schema: "sales",
            table: "InvoiceLines");

        migrationBuilder.DropColumn(
            name: "DocumentLineId",
            schema: "inventory",
            table: "StockMovements");

        migrationBuilder.DropColumn(
            name: "ReturnOfInvoiceId",
            schema: "sales",
            table: "Invoices");

        migrationBuilder.DropColumn(
            name: "SourceLineId",
            schema: "sales",
            table: "InvoiceLines");
    }
}
