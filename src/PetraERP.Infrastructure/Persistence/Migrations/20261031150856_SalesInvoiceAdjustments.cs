using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace PetraERP.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class SalesInvoiceAdjustments : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "InvoiceAdjustments",
            schema: "sales",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SalesInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Kind = table.Column<int>(type: "int", nullable: false),
                AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                Percentage = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                CostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                DisplayOrder = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_InvoiceAdjustments", x => x.Id);
                table.CheckConstraint("CK_SalesInvoiceAdjustments_Amount", "[Amount] > 0");
                table.CheckConstraint("CK_SalesInvoiceAdjustments_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_SalesInvoiceAdjustments_Kind", "[Kind] IN (1, 2)");
                table.CheckConstraint("CK_SalesInvoiceAdjustments_Percentage", "[Percentage] >= 0 AND [Percentage] <= 100");
                table.ForeignKey(
                    name: "FK_InvoiceAdjustments_Accounts_AccountId",
                    column: x => x.AccountId,
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
                    name: "FK_InvoiceAdjustments_Invoices_SalesInvoiceId",
                    column: x => x.SalesInvoiceId,
                    principalSchema: "sales",
                    principalTable: "Invoices",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_InvoiceAdjustments_AccountId",
            schema: "sales",
            table: "InvoiceAdjustments",
            column: "AccountId");

        migrationBuilder.CreateIndex(
            name: "IX_InvoiceAdjustments_CostCenterId",
            schema: "sales",
            table: "InvoiceAdjustments",
            column: "CostCenterId");

        migrationBuilder.CreateIndex(
            name: "IX_SalesInvoiceAdjustments_InvoiceId",
            schema: "sales",
            table: "InvoiceAdjustments",
            column: "SalesInvoiceId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "InvoiceAdjustments",
            schema: "sales");
    }
}
