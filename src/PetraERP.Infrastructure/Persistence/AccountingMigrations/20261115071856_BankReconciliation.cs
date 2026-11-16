using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace PetraERP.Infrastructure.Persistence.AccountingMigrations;

/// <inheritdoc />
public partial class BankReconciliation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "BankReconciliations",
            schema: "treasury",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                FiscalYearId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                StatementDate = table.Column<DateOnly>(type: "date", nullable: false),
                StatementBalance = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompletedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BankReconciliations", x => x.Id);
                table.CheckConstraint("CK_BankReconciliations_Completion", "([Status]=N'Completed' AND [CompletedAtUtc] IS NOT NULL AND [CompletedByUserId] IS NOT NULL) OR ([Status]=N'Draft' AND [CompletedAtUtc] IS NULL AND [CompletedByUserId] IS NULL)");
                table.CheckConstraint("CK_BankReconciliations_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_BankReconciliations_Status", "[Status] IN (N'Draft',N'Completed')");
                table.CheckConstraint("CK_BankReconciliations_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_BankReconciliations_Accounts_AccountId",
                    column: x => x.AccountId,
                    principalSchema: "ledger",
                    principalTable: "Accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_BankReconciliations_ActorReferences_CompanyId_CompletedByUserId",
                    columns: x => new { x.CompanyId, x.CompletedByUserId },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_BankReconciliations_ActorReferences_CompanyId_CreatedByUserId",
                    columns: x => new { x.CompanyId, x.CreatedByUserId },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_BankReconciliations_Branches_CompanyId_BranchId",
                    columns: x => new { x.CompanyId, x.BranchId },
                    principalSchema: "system",
                    principalTable: "Branches",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_BankReconciliations_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_BankReconciliations_FiscalYears_CompanyId_FiscalYearId",
                    columns: x => new { x.CompanyId, x.FiscalYearId },
                    principalSchema: "ledger",
                    principalTable: "FiscalYears",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "BankReconciliationLines",
            schema: "treasury",
            columns: table => new
            {
                ReconciliationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                JournalLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BankReconciliationLines", x => new { x.ReconciliationId, x.JournalLineId });
                table.ForeignKey(
                    name: "FK_BankReconciliationLines_BankReconciliations_ReconciliationId",
                    column: x => x.ReconciliationId,
                    principalSchema: "treasury",
                    principalTable: "BankReconciliations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_BankReconciliationLines_JournalLines_JournalLineId",
                    column: x => x.JournalLineId,
                    principalSchema: "ledger",
                    principalTable: "JournalLines",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_BankReconciliationLines_JournalLineId",
            schema: "treasury",
            table: "BankReconciliationLines",
            column: "JournalLineId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_BankReconciliations_AccountId",
            schema: "treasury",
            table: "BankReconciliations",
            column: "AccountId");

        migrationBuilder.CreateIndex(
            name: "IX_BankReconciliations_CompanyId_BranchId_AccountId",
            schema: "treasury",
            table: "BankReconciliations",
            columns: new[] { "CompanyId", "BranchId", "AccountId" },
            unique: true,
            filter: "[Status] = N'Draft'");

        migrationBuilder.CreateIndex(
            name: "IX_BankReconciliations_CompanyId_BranchId_AccountId_StatementDate",
            schema: "treasury",
            table: "BankReconciliations",
            columns: new[] { "CompanyId", "BranchId", "AccountId", "StatementDate" });

        migrationBuilder.CreateIndex(
            name: "IX_BankReconciliations_CompanyId_CompletedByUserId",
            schema: "treasury",
            table: "BankReconciliations",
            columns: new[] { "CompanyId", "CompletedByUserId" });

        migrationBuilder.CreateIndex(
            name: "IX_BankReconciliations_CompanyId_CreatedByUserId",
            schema: "treasury",
            table: "BankReconciliations",
            columns: new[] { "CompanyId", "CreatedByUserId" });

        migrationBuilder.CreateIndex(
            name: "IX_BankReconciliations_CompanyId_FiscalYearId",
            schema: "treasury",
            table: "BankReconciliations",
            columns: new[] { "CompanyId", "FiscalYearId" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "BankReconciliationLines",
            schema: "treasury");

        migrationBuilder.DropTable(
            name: "BankReconciliations",
            schema: "treasury");
    }
}
