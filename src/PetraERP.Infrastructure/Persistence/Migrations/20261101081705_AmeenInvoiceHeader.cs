using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace PetraERP.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AmeenInvoiceHeader : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "DistributorCode",
            schema: "sales",
            table: "Invoices",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "SellerCode",
            schema: "sales",
            table: "Invoices",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "DistributorCode",
            schema: "purchases",
            table: "Invoices",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "SellerCode",
            schema: "purchases",
            table: "Invoices",
            type: "int",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_SalesInvoices_DistributorCode",
            schema: "sales",
            table: "Invoices",
            sql: "[DistributorCode] IS NULL OR [DistributorCode] >= 0");

        migrationBuilder.AddCheckConstraint(
            name: "CK_SalesInvoices_SellerCode",
            schema: "sales",
            table: "Invoices",
            sql: "[SellerCode] IS NULL OR [SellerCode] >= 0");

        migrationBuilder.AddCheckConstraint(
            name: "CK_PurchaseInvoices_DistributorCode",
            schema: "purchases",
            table: "Invoices",
            sql: "[DistributorCode] IS NULL OR [DistributorCode] >= 0");

        migrationBuilder.AddCheckConstraint(
            name: "CK_PurchaseInvoices_SellerCode",
            schema: "purchases",
            table: "Invoices",
            sql: "[SellerCode] IS NULL OR [SellerCode] >= 0");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_SalesInvoices_DistributorCode",
            schema: "sales",
            table: "Invoices");

        migrationBuilder.DropCheckConstraint(
            name: "CK_SalesInvoices_SellerCode",
            schema: "sales",
            table: "Invoices");

        migrationBuilder.DropCheckConstraint(
            name: "CK_PurchaseInvoices_DistributorCode",
            schema: "purchases",
            table: "Invoices");

        migrationBuilder.DropCheckConstraint(
            name: "CK_PurchaseInvoices_SellerCode",
            schema: "purchases",
            table: "Invoices");

        migrationBuilder.DropColumn(
            name: "DistributorCode",
            schema: "sales",
            table: "Invoices");

        migrationBuilder.DropColumn(
            name: "SellerCode",
            schema: "sales",
            table: "Invoices");

        migrationBuilder.DropColumn(
            name: "DistributorCode",
            schema: "purchases",
            table: "Invoices");

        migrationBuilder.DropColumn(
            name: "SellerCode",
            schema: "purchases",
            table: "Invoices");
    }
}
