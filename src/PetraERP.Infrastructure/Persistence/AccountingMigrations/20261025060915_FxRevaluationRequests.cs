using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetraERP.Infrastructure.Persistence.AccountingMigrations;

/// <inheritdoc />
public partial class FxRevaluationRequests : Migration
{
    private static readonly string[] ActorPrincipalColumns = ["CompanyId", "Id"];
    private static readonly string[] ActorIndexColumns = ["CompanyId", "ActorUserId"];
    private static readonly string[] ScopeKeyColumns = ["CompanyId", "BranchId", "IdempotencyKey"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "FxRevaluationRequests",
            schema: "ledger",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                FiscalYearId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                FingerprintHex = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Status = table.Column<byte>(type: "tinyint", nullable: false),
                DraftJournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                OutcomeJournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                RegisteredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FxRevaluationRequests", x => x.Id);
                table.CheckConstraint("CK_FxRevaluationRequests_ActorUserId", "[ActorUserId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_FxRevaluationRequests_BranchId", "[BranchId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_FxRevaluationRequests_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_FxRevaluationRequests_Completed", "([Status] = 2 AND [OutcomeJournalId] IS NOT NULL AND [CompletedAtUtc] IS NOT NULL) OR ([Status] = 1)");
                table.CheckConstraint("CK_FxRevaluationRequests_CompletedAtUtc", "[CompletedAtUtc] IS NULL OR DATEPART(TZOFFSET, [CompletedAtUtc]) = 0");
                table.CheckConstraint("CK_FxRevaluationRequests_FingerprintHex", "LEN([FingerprintHex]) = 64");
                table.CheckConstraint("CK_FxRevaluationRequests_FiscalYearId", "[FiscalYearId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_FxRevaluationRequests_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_FxRevaluationRequests_IdempotencyKey", "LEN([IdempotencyKey]) > 0");
                table.CheckConstraint("CK_FxRevaluationRequests_RegisteredAtUtc", "DATEPART(TZOFFSET, [RegisteredAtUtc]) = 0");
                table.CheckConstraint("CK_FxRevaluationRequests_Status", "[Status] IN (1, 2)");
                table.ForeignKey(
                    name: "FK_FxRevaluationRequests_ActorReferences_CompanyId_ActorUserId",
                    columns: x => new { x.CompanyId, x.ActorUserId },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: ActorPrincipalColumns,
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_FxRevaluationRequests_CompanyId_ActorUserId",
            schema: "ledger",
            table: "FxRevaluationRequests",
            columns: ActorIndexColumns);

        migrationBuilder.CreateIndex(
            name: "UQ_FxRevaluationRequests_Scope_Key",
            schema: "ledger",
            table: "FxRevaluationRequests",
            columns: ScopeKeyColumns,
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "FxRevaluationRequests",
            schema: "ledger");
    }
}
