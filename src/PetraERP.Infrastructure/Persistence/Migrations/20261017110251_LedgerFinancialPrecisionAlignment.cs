using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetraERP.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class LedgerFinancialPrecisionAlignment : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (
                SELECT 1 FROM [ledger].[JournalLines]
                WHERE [TransactionDebit] <> ROUND([TransactionDebit], 4)
                   OR [TransactionCredit] <> ROUND([TransactionCredit], 4)
                   OR [BaseDebit] <> ROUND([BaseDebit], 4)
                   OR [BaseCredit] <> ROUND([BaseCredit], 4)
            )
                THROW 51005, 'Upgrade blocked: JournalLines contain monetary amounts with fractional precision exceeding 4 decimal places. Data must be reconciled before altering precision.', 1;

            IF EXISTS (
                SELECT 1 FROM [ledger].[JournalLines]
                WHERE [RateValue] <> ROUND([RateValue], 8)
            )
                THROW 51006, 'Upgrade blocked: JournalLines contain exchange rates with fractional precision exceeding 8 decimal places. Data must be reconciled before altering precision.', 1;
            """);

        migrationBuilder.AlterColumn<decimal>(
            name: "TransactionDebit",
            schema: "ledger",
            table: "JournalLines",
            type: "decimal(19,4)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,6)");

        migrationBuilder.AlterColumn<decimal>(
            name: "TransactionCredit",
            schema: "ledger",
            table: "JournalLines",
            type: "decimal(19,4)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,6)");

        migrationBuilder.AlterColumn<decimal>(
            name: "RateValue",
            schema: "ledger",
            table: "JournalLines",
            type: "decimal(19,8)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,10)");

        migrationBuilder.AlterColumn<decimal>(
            name: "BaseDebit",
            schema: "ledger",
            table: "JournalLines",
            type: "decimal(19,4)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,6)");

        migrationBuilder.AlterColumn<decimal>(
            name: "BaseCredit",
            schema: "ledger",
            table: "JournalLines",
            type: "decimal(19,4)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,6)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (
                SELECT 1 FROM [ledger].[JournalLines]
                WHERE [TransactionDebit] >= 1000000000000
                   OR [TransactionCredit] >= 1000000000000
                   OR [BaseDebit] >= 1000000000000
                   OR [BaseCredit] >= 1000000000000
            )
                THROW 51007, 'Downgrade blocked: JournalLines contain monetary amounts exceeding decimal(18,6) range (12 integer digits). No data was changed.', 1;

            IF EXISTS (
                SELECT 1 FROM [ledger].[JournalLines]
                WHERE [RateValue] >= 100000000
            )
                THROW 51008, 'Downgrade blocked: JournalLines contain exchange rates exceeding decimal(18,10) range (8 integer digits). No data was changed.', 1;
            """);

        migrationBuilder.AlterColumn<decimal>(
            name: "TransactionDebit",
            schema: "ledger",
            table: "JournalLines",
            type: "decimal(18,6)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(19,4)");

        migrationBuilder.AlterColumn<decimal>(
            name: "TransactionCredit",
            schema: "ledger",
            table: "JournalLines",
            type: "decimal(18,6)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(19,4)");

        migrationBuilder.AlterColumn<decimal>(
            name: "RateValue",
            schema: "ledger",
            table: "JournalLines",
            type: "decimal(18,10)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(19,8)");

        migrationBuilder.AlterColumn<decimal>(
            name: "BaseDebit",
            schema: "ledger",
            table: "JournalLines",
            type: "decimal(18,6)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(19,4)");

        migrationBuilder.AlterColumn<decimal>(
            name: "BaseCredit",
            schema: "ledger",
            table: "JournalLines",
            type: "decimal(18,6)",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(19,4)");
    }
}
