using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace PetraERP.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class LedgerCoveringIndexes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_JournalLines_CompanyAccountJournal",
            schema: "ledger",
            table: "JournalLines");

        migrationBuilder.DropIndex(
            name: "IX_JournalEntries_CompanyBranchStateDate",
            schema: "ledger",
            table: "JournalEntries");

        migrationBuilder.CreateIndex(
            name: "IX_JournalLines_CompanyAccountJournal",
            schema: "ledger",
            table: "JournalLines",
            columns: new[] { "CompanyId", "AccountId", "JournalId" })
            .Annotation("SqlServer:Include", new[] { "BaseDebit", "BaseCredit" });

        migrationBuilder.CreateIndex(
            name: "IX_JournalEntries_CompanyBranchStateDate",
            schema: "ledger",
            table: "JournalEntries",
            columns: new[] { "CompanyId", "BranchId", "State", "AccountingDate", "PostingSequence" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_JournalLines_CompanyAccountJournal",
            schema: "ledger",
            table: "JournalLines");

        migrationBuilder.DropIndex(
            name: "IX_JournalEntries_CompanyBranchStateDate",
            schema: "ledger",
            table: "JournalEntries");

        migrationBuilder.CreateIndex(
            name: "IX_JournalLines_CompanyAccountJournal",
            schema: "ledger",
            table: "JournalLines",
            columns: new[] { "CompanyId", "AccountId", "JournalId" });

        migrationBuilder.CreateIndex(
            name: "IX_JournalEntries_CompanyBranchStateDate",
            schema: "ledger",
            table: "JournalEntries",
            columns: new[] { "CompanyId", "BranchId", "State", "AccountingDate" });
    }
}
