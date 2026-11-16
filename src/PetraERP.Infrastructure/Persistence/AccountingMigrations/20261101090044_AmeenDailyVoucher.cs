using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace PetraERP.Infrastructure.Persistence.AccountingMigrations;

/// <inheritdoc />
public partial class AmeenDailyVoucher : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "JournalVoucherHeaders",
            schema: "ledger",
            columns: table => new
            {
                JournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                CashAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_JournalVoucherHeaders", x => x.JournalId);
                table.CheckConstraint("CK_JournalVoucherHeaders_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_JournalVoucherHeaders_Kind", "[Kind] IN (N'DAILY')");
                table.CheckConstraint("CK_JournalVoucherHeaders_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_JournalVoucherHeaders_Accounts_CashAccountId",
                    column: x => x.CashAccountId,
                    principalSchema: "ledger",
                    principalTable: "Accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_JournalVoucherHeaders_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_JournalVoucherHeaders_JournalEntries_JournalId",
                    column: x => x.JournalId,
                    principalSchema: "ledger",
                    principalTable: "JournalEntries",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_JournalVoucherHeaders_CashAccount",
            schema: "ledger",
            table: "JournalVoucherHeaders",
            column: "CashAccountId");

        migrationBuilder.CreateIndex(
            name: "IX_JournalVoucherHeaders_CompanyKind",
            schema: "ledger",
            table: "JournalVoucherHeaders",
            columns: new[] { "CompanyId", "Kind" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "JournalVoucherHeaders",
            schema: "ledger");
    }
}
