using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetraERP.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class SalesPostingRequestIntegrity : Migration
{
    private static readonly string[] InvoiceScope = ["CompanyId", "BranchId", "InvoiceId"];
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PostingRequests",
            schema: "sales",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Operation = table.Column<byte>(type: "tinyint", nullable: false),
                IdempotencyKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ExpectedInvoiceVersion = table.Column<byte[]>(type: "binary(8)", nullable: false),
                Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                FingerprintHex = table.Column<string>(type: "char(64)", nullable: false),
                RegisteredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                OutcomeJournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PostingRequests", x => new { x.CompanyId, x.BranchId, x.Operation, x.IdempotencyKey });
                table.CheckConstraint("CK_SalesPostingRequests_Completion", "([CompletedAtUtc] IS NULL AND [OutcomeJournalId] IS NULL) OR ([CompletedAtUtc] IS NOT NULL AND [OutcomeJournalId] IS NOT NULL)");
                table.CheckConstraint("CK_SalesPostingRequests_Key", "[IdempotencyKey]<>'00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_SalesPostingRequests_Operation", "[Operation] IN (1,2)");
                table.CheckConstraint("CK_SalesPostingRequests_Version", "DATALENGTH([ExpectedInvoiceVersion])=8");
                table.ForeignKey(
                    name: "FK_PostingRequests_Branches_BranchId",
                    column: x => x.BranchId,
                    principalSchema: "system",
                    principalTable: "Branches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PostingRequests_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PostingRequests_Invoices_InvoiceId",
                    column: x => x.InvoiceId,
                    principalSchema: "sales",
                    principalTable: "Invoices",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PostingRequests_JournalEntries_OutcomeJournalId",
                    column: x => x.OutcomeJournalId,
                    principalSchema: "ledger",
                    principalTable: "JournalEntries",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PostingRequests_Users_ActorUserId",
                    column: x => x.ActorUserId,
                    principalSchema: "security",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Invoices_JournalEntryId",
            schema: "sales",
            table: "Invoices",
            column: "JournalEntryId",
            unique: true,
            filter: "[JournalEntryId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Invoices_ReversedByInvoiceId",
            schema: "sales",
            table: "Invoices",
            column: "ReversedByInvoiceId");

        migrationBuilder.CreateIndex(
            name: "IX_Invoices_ReversesInvoiceId",
            schema: "sales",
            table: "Invoices",
            column: "ReversesInvoiceId",
            unique: true,
            filter: "[ReversesInvoiceId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_PostingRequests_ActorUserId",
            schema: "sales",
            table: "PostingRequests",
            column: "ActorUserId");

        migrationBuilder.CreateIndex(
            name: "IX_PostingRequests_BranchId",
            schema: "sales",
            table: "PostingRequests",
            column: "BranchId");

        migrationBuilder.CreateIndex(
            name: "IX_PostingRequests_CompanyId_BranchId_InvoiceId",
            schema: "sales",
            table: "PostingRequests",
            columns: InvoiceScope);

        migrationBuilder.CreateIndex(
            name: "IX_PostingRequests_InvoiceId",
            schema: "sales",
            table: "PostingRequests",
            column: "InvoiceId");

        migrationBuilder.CreateIndex(
            name: "IX_PostingRequests_OutcomeJournalId",
            schema: "sales",
            table: "PostingRequests",
            column: "OutcomeJournalId");

        migrationBuilder.AddForeignKey(
            name: "FK_Invoices_Invoices_ReversedByInvoiceId",
            schema: "sales",
            table: "Invoices",
            column: "ReversedByInvoiceId",
            principalSchema: "sales",
            principalTable: "Invoices",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_Invoices_Invoices_ReversesInvoiceId",
            schema: "sales",
            table: "Invoices",
            column: "ReversesInvoiceId",
            principalSchema: "sales",
            principalTable: "Invoices",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_Invoices_JournalEntries_JournalEntryId",
            schema: "sales",
            table: "Invoices",
            column: "JournalEntryId",
            principalSchema: "ledger",
            principalTable: "JournalEntries",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Invoices_Invoices_ReversedByInvoiceId",
            schema: "sales",
            table: "Invoices");

        migrationBuilder.DropForeignKey(
            name: "FK_Invoices_Invoices_ReversesInvoiceId",
            schema: "sales",
            table: "Invoices");

        migrationBuilder.DropForeignKey(
            name: "FK_Invoices_JournalEntries_JournalEntryId",
            schema: "sales",
            table: "Invoices");

        migrationBuilder.DropTable(
            name: "PostingRequests",
            schema: "sales");

        migrationBuilder.DropIndex(
            name: "IX_Invoices_JournalEntryId",
            schema: "sales",
            table: "Invoices");

        migrationBuilder.DropIndex(
            name: "IX_Invoices_ReversedByInvoiceId",
            schema: "sales",
            table: "Invoices");

        migrationBuilder.DropIndex(
            name: "IX_Invoices_ReversesInvoiceId",
            schema: "sales",
            table: "Invoices");
    }
}
