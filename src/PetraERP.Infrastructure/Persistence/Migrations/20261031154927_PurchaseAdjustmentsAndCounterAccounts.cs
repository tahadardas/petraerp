using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace PetraERP.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class PurchaseAdjustmentsAndCounterAccounts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_PurchaseInvoices_Totals",
            schema: "purchases",
            table: "Invoices");

        migrationBuilder.AddColumn<decimal>(
            name: "AdditionsTotal",
            schema: "purchases",
            table: "Invoices",
            type: "decimal(19,4)",
            precision: 19,
            scale: 4,
            nullable: false,
            defaultValueSql: "0.0");

        migrationBuilder.AddColumn<Guid>(
            name: "CounterAccountId",
            schema: "sales",
            table: "InvoiceAdjustments",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "InvoiceAdjustments",
            schema: "purchases",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PurchaseInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Kind = table.Column<int>(type: "int", nullable: false),
                AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CounterAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                Percentage = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                CostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                DisplayOrder = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_InvoiceAdjustments", x => x.Id);
                table.CheckConstraint("CK_PurchaseInvoiceAdjustments_Amount", "[Amount] > 0");
                table.CheckConstraint("CK_PurchaseInvoiceAdjustments_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_PurchaseInvoiceAdjustments_Kind", "[Kind] IN (1, 2)");
                table.CheckConstraint("CK_PurchaseInvoiceAdjustments_Percentage", "[Percentage] >= 0 AND [Percentage] <= 100");
                table.ForeignKey(
                    name: "FK_InvoiceAdjustments_Accounts_AccountId",
                    column: x => x.AccountId,
                    principalSchema: "ledger",
                    principalTable: "Accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_InvoiceAdjustments_Accounts_CounterAccountId",
                    column: x => x.CounterAccountId,
                    principalSchema: "ledger",
                    principalTable: "Accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_InvoiceAdjustments_CostCenters_CostCenterId",
                    column: x => x.CostCenterId,
                    principalSchema: "ledger",
                    principalTable: "CostCenters",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_InvoiceAdjustments_Invoices_PurchaseInvoiceId",
                    column: x => x.PurchaseInvoiceId,
                    principalSchema: "purchases",
                    principalTable: "Invoices",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.AddCheckConstraint(
            name: "CK_PurchaseInvoices_Totals",
            schema: "purchases",
            table: "Invoices",
            sql: "[SubTotal] >= 0 AND [DiscountTotal] >= 0 AND [TaxTotal] >= 0 AND [AdditionsTotal] >= 0 AND [GrandTotal] = [SubTotal] - [DiscountTotal] + [TaxTotal] + [AdditionsTotal]");

        migrationBuilder.CreateIndex(
            name: "IX_InvoiceAdjustments_CounterAccountId",
            schema: "sales",
            table: "InvoiceAdjustments",
            column: "CounterAccountId");

        migrationBuilder.CreateIndex(
            name: "IX_InvoiceAdjustments_AccountId",
            schema: "purchases",
            table: "InvoiceAdjustments",
            column: "AccountId");

        migrationBuilder.CreateIndex(
            name: "IX_InvoiceAdjustments_CostCenterId",
            schema: "purchases",
            table: "InvoiceAdjustments",
            column: "CostCenterId");

        migrationBuilder.CreateIndex(
            name: "IX_InvoiceAdjustments_CounterAccountId",
            schema: "purchases",
            table: "InvoiceAdjustments",
            column: "CounterAccountId");

        migrationBuilder.CreateIndex(
            name: "IX_PurchaseInvoiceAdjustments_InvoiceId",
            schema: "purchases",
            table: "InvoiceAdjustments",
            column: "PurchaseInvoiceId");

        migrationBuilder.AddForeignKey(
            name: "FK_InvoiceAdjustments_Accounts_CounterAccountId",
            schema: "sales",
            table: "InvoiceAdjustments",
            column: "CounterAccountId",
            principalSchema: "ledger",
            principalTable: "Accounts",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_InvoiceAdjustments_Accounts_CounterAccountId",
            schema: "sales",
            table: "InvoiceAdjustments");

        migrationBuilder.DropTable(
            name: "InvoiceAdjustments",
            schema: "purchases");

        migrationBuilder.DropCheckConstraint(
            name: "CK_PurchaseInvoices_Totals",
            schema: "purchases",
            table: "Invoices");

        migrationBuilder.DropIndex(
            name: "IX_InvoiceAdjustments_CounterAccountId",
            schema: "sales",
            table: "InvoiceAdjustments");

        migrationBuilder.DropColumn(
            name: "AdditionsTotal",
            schema: "purchases",
            table: "Invoices");

        migrationBuilder.DropColumn(
            name: "CounterAccountId",
            schema: "sales",
            table: "InvoiceAdjustments");

        migrationBuilder.AddCheckConstraint(
            name: "CK_PurchaseInvoices_Totals",
            schema: "purchases",
            table: "Invoices",
            sql: "[SubTotal] >= 0 AND [DiscountTotal] >= 0 AND [TaxTotal] >= 0 AND [GrandTotal] = [SubTotal] - [DiscountTotal] + [TaxTotal]");
    }
}
