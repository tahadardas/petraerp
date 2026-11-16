using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1861

namespace PetraERP.Infrastructure.Persistence.AccountingMigrations;
    /// <inheritdoc />
    public partial class DocumentPostingMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowSaveAsDraft",
                schema: "policy",
                table: "CompanyPolicies",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<byte>(
                name: "DocumentPostingMode",
                schema: "policy",
                table: "CompanyPolicies",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddCheckConstraint(
                name: "CK_CompanyPolicies_PostingMode",
                schema: "policy",
                table: "CompanyPolicies",
                sql: "[DocumentPostingMode] IN (0,1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CompanyPolicies_PostingMode",
                schema: "policy",
                table: "CompanyPolicies");

            migrationBuilder.DropColumn(
                name: "AllowSaveAsDraft",
                schema: "policy",
                table: "CompanyPolicies");

            migrationBuilder.DropColumn(
                name: "DocumentPostingMode",
                schema: "policy",
                table: "CompanyPolicies");
        }
    }
