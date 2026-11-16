using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace PetraERP.Infrastructure.Persistence.AccountingMigrations;

/// <inheritdoc />
public partial class PurchaseInvoiceReversalLinks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "ReversedByInvoiceId",
            schema: "purchases",
            table: "Invoices",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "ReversesInvoiceId",
            schema: "purchases",
            table: "Invoices",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Invoices_ReversedByInvoiceId",
            schema: "purchases",
            table: "Invoices",
            column: "ReversedByInvoiceId");

        migrationBuilder.CreateIndex(
            name: "IX_PurchaseInvoices_Reverses",
            schema: "purchases",
            table: "Invoices",
            column: "ReversesInvoiceId",
            unique: true,
            filter: "[ReversesInvoiceId] IS NOT NULL");

        migrationBuilder.AddCheckConstraint(
            name: "CK_PurchaseInvoices_ReversalKind",
            schema: "purchases",
            table: "Invoices",
            sql: "[ReversesInvoiceId] IS NULL OR ([ReturnOfInvoiceId] IS NULL AND [ReversedByInvoiceId] IS NULL AND [ReversesInvoiceId] <> [Id] AND [Status] = 2 AND [JournalEntryId] IS NOT NULL AND [InvoiceNumber] IS NOT NULL)");

        migrationBuilder.AddCheckConstraint(
            name: "CK_PurchaseInvoices_ReversedLink",
            schema: "purchases",
            table: "Invoices",
            sql: "[ReversedByInvoiceId] IS NULL OR [Status] = 3");

        migrationBuilder.AddForeignKey(
            name: "FK_Invoices_Invoices_ReversedByInvoiceId",
            schema: "purchases",
            table: "Invoices",
            column: "ReversedByInvoiceId",
            principalSchema: "purchases",
            principalTable: "Invoices",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_Invoices_Invoices_ReversesInvoiceId",
            schema: "purchases",
            table: "Invoices",
            column: "ReversesInvoiceId",
            principalSchema: "purchases",
            principalTable: "Invoices",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Invoices_Invoices_ReversedByInvoiceId",
            schema: "purchases",
            table: "Invoices");

        migrationBuilder.DropForeignKey(
            name: "FK_Invoices_Invoices_ReversesInvoiceId",
            schema: "purchases",
            table: "Invoices");

        migrationBuilder.DropIndex(
            name: "IX_Invoices_ReversedByInvoiceId",
            schema: "purchases",
            table: "Invoices");

        migrationBuilder.DropIndex(
            name: "IX_PurchaseInvoices_Reverses",
            schema: "purchases",
            table: "Invoices");

        migrationBuilder.DropCheckConstraint(
            name: "CK_PurchaseInvoices_ReversalKind",
            schema: "purchases",
            table: "Invoices");

        migrationBuilder.DropCheckConstraint(
            name: "CK_PurchaseInvoices_ReversedLink",
            schema: "purchases",
            table: "Invoices");

        migrationBuilder.DropColumn(
            name: "ReversedByInvoiceId",
            schema: "purchases",
            table: "Invoices");

        migrationBuilder.DropColumn(
            name: "ReversesInvoiceId",
            schema: "purchases",
            table: "Invoices");
    }
}
