using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace PetraERP.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InvoiceAccountAndPartyParents : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "CustomerAccountId",
            schema: "sales",
            table: "Invoices",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "CustomersParentAccountId",
            schema: "policy",
            table: "CompanyPolicies",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "SuppliersParentAccountId",
            schema: "policy",
            table: "CompanyPolicies",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Invoices_CustomerAccountId",
            schema: "sales",
            table: "Invoices",
            column: "CustomerAccountId");

        migrationBuilder.CreateIndex(
            name: "IX_CompanyPolicies_CustomersParentAccountId",
            schema: "policy",
            table: "CompanyPolicies",
            column: "CustomersParentAccountId");

        migrationBuilder.CreateIndex(
            name: "IX_CompanyPolicies_SuppliersParentAccountId",
            schema: "policy",
            table: "CompanyPolicies",
            column: "SuppliersParentAccountId");

        migrationBuilder.AddForeignKey(
            name: "FK_CompanyPolicies_Accounts_CustomersParentAccountId",
            schema: "policy",
            table: "CompanyPolicies",
            column: "CustomersParentAccountId",
            principalSchema: "ledger",
            principalTable: "Accounts",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_CompanyPolicies_Accounts_SuppliersParentAccountId",
            schema: "policy",
            table: "CompanyPolicies",
            column: "SuppliersParentAccountId",
            principalSchema: "ledger",
            principalTable: "Accounts",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_Invoices_Accounts_CustomerAccountId",
            schema: "sales",
            table: "Invoices",
            column: "CustomerAccountId",
            principalSchema: "ledger",
            principalTable: "Accounts",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_CompanyPolicies_Accounts_CustomersParentAccountId",
            schema: "policy",
            table: "CompanyPolicies");

        migrationBuilder.DropForeignKey(
            name: "FK_CompanyPolicies_Accounts_SuppliersParentAccountId",
            schema: "policy",
            table: "CompanyPolicies");

        migrationBuilder.DropForeignKey(
            name: "FK_Invoices_Accounts_CustomerAccountId",
            schema: "sales",
            table: "Invoices");

        migrationBuilder.DropIndex(
            name: "IX_Invoices_CustomerAccountId",
            schema: "sales",
            table: "Invoices");

        migrationBuilder.DropIndex(
            name: "IX_CompanyPolicies_CustomersParentAccountId",
            schema: "policy",
            table: "CompanyPolicies");

        migrationBuilder.DropIndex(
            name: "IX_CompanyPolicies_SuppliersParentAccountId",
            schema: "policy",
            table: "CompanyPolicies");

        migrationBuilder.DropColumn(
            name: "CustomerAccountId",
            schema: "sales",
            table: "Invoices");

        migrationBuilder.DropColumn(
            name: "CustomersParentAccountId",
            schema: "policy",
            table: "CompanyPolicies");

        migrationBuilder.DropColumn(
            name: "SuppliersParentAccountId",
            schema: "policy",
            table: "CompanyPolicies");
    }
}
