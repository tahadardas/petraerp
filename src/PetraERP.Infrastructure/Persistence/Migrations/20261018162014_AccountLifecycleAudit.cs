using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetraERP.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AccountLifecycleAudit : Migration
{
    private static readonly string[] HistoryColumns = ["CompanyId", "AccountId", "OccurredAtUtc", "EventId"];
    private static readonly string[] BranchColumns = ["CompanyId", "WorkingBranchId"];
    private static readonly string[] BranchKey = ["CompanyId", "Id"];
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Reviewed additive change: retain legacy deletion events, add typed state snapshots and working-branch context.
        migrationBuilder.DropCheckConstraint(
            name: "CK_AccountAudit_Action",
            schema: "ledger",
            table: "AccountAudit");

        migrationBuilder.DropCheckConstraint(
            name: "CK_AccountAudit_Reason",
            schema: "ledger",
            table: "AccountAudit");

        migrationBuilder.AlterColumn<string>(
            name: "Reason",
            schema: "ledger",
            table: "AccountAudit",
            type: "nvarchar(1000)",
            maxLength: 1000,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "nvarchar(1000)",
            oldMaxLength: 1000);

        migrationBuilder.AddColumn<Guid>(
            name: "WorkingBranchId",
            schema: "ledger",
            table: "AccountAudit",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "AccountAuditStates",
            schema: "ledger",
            columns: table => new
            {
                EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Stage = table.Column<byte>(type: "tinyint", nullable: false),
                Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                NameArabic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                NameEnglish = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                AccountType = table.Column<byte>(type: "tinyint", nullable: false),
                PostingAllowed = table.Column<bool>(type: "bit", nullable: false),
                CostCenterPolicy = table.Column<byte>(type: "tinyint", nullable: false),
                CurrencyMode = table.Column<byte>(type: "tinyint", nullable: false),
                FixedCurrencyCode = table.Column<string>(type: "char(3)", nullable: true, collation: "Latin1_General_100_BIN2"),
                ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                IsActive = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AccountAuditStates", x => new { x.EventId, x.Stage });
                table.CheckConstraint("CK_AccountAuditStates_Code", "DATALENGTH([Code]) > 0 AND DATALENGTH([Code]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Code]))");
                table.CheckConstraint("CK_AccountAuditStates_Currency", "([CurrencyMode] = 3 AND [FixedCurrencyCode] IS NOT NULL) OR ([CurrencyMode] <> 3 AND [FixedCurrencyCode] IS NULL)");
                table.CheckConstraint("CK_AccountAuditStates_Names", "(DATALENGTH([NameArabic]) > 0 AND DATALENGTH([NameArabic]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [NameArabic]))) AND ([NameEnglish] IS NULL OR (DATALENGTH([NameEnglish]) > 0 AND DATALENGTH([NameEnglish]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [NameEnglish]))))");
                table.CheckConstraint("CK_AccountAuditStates_Stage", "[Stage] IN (1, 2)");
                table.CheckConstraint("CK_AccountAuditStates_Types", "[AccountType] BETWEEN 1 AND 5 AND [CostCenterPolicy] BETWEEN 1 AND 3 AND [CurrencyMode] BETWEEN 1 AND 3");
                table.ForeignKey(
                    name: "FK_AccountAuditStates_AccountAudit_EventId",
                    column: x => x.EventId,
                    principalSchema: "ledger",
                    principalTable: "AccountAudit",
                    principalColumn: "EventId",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AccountAudit_AccountHistory",
            schema: "ledger",
            table: "AccountAudit",
            columns: HistoryColumns);

        migrationBuilder.CreateIndex(
            name: "IX_AccountAudit_CompanyId_WorkingBranchId",
            schema: "ledger",
            table: "AccountAudit",
            columns: BranchColumns);

        migrationBuilder.AddCheckConstraint(
            name: "CK_AccountAudit_Action",
            schema: "ledger",
            table: "AccountAudit",
            sql: "[Action] IN (1, 2, 3, 4, 5)");

        migrationBuilder.AddCheckConstraint(
            name: "CK_AccountAudit_Reason",
            schema: "ledger",
            table: "AccountAudit",
            sql: "([Reason] IS NULL OR (DATALENGTH([Reason]) > 0 AND DATALENGTH([Reason]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Reason])))) AND ([Action] <> 1 OR [Reason] IS NOT NULL)");

        migrationBuilder.AddForeignKey(
            name: "FK_AccountAudit_Branches_CompanyId_WorkingBranchId",
            schema: "ledger",
            table: "AccountAudit",
            columns: BranchColumns,
            principalSchema: "system",
            principalTable: "Branches",
            principalColumns: BranchKey,
            onDelete: ReferentialAction.Restrict);
        migrationBuilder.Sql("""
            EXEC(N'CREATE TRIGGER [ledger].[TR_AccountAuditStates_AppendOnly]
            ON [ledger].[AccountAuditStates] INSTEAD OF UPDATE, DELETE AS
            BEGIN
                SET NOCOUNT ON;
                THROW 51012, ''Account state audit is append-only and cannot be updated or deleted.'', 1;
            END;');
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // A downgrade may remove empty schema, but never discard recorded lifecycle evidence or its branch context.
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [ledger].[AccountAuditStates])
                OR EXISTS (SELECT 1 FROM [ledger].[AccountAudit] WHERE [Action] <> 1 OR [WorkingBranchId] IS NOT NULL)
                THROW 51013, 'Account lifecycle audit has recorded data; restore a reviewed backup instead of discarding audit history.', 1;
            DROP TRIGGER IF EXISTS [ledger].[TR_AccountAuditStates_AppendOnly];
            """);
        migrationBuilder.DropForeignKey(
            name: "FK_AccountAudit_Branches_CompanyId_WorkingBranchId",
            schema: "ledger",
            table: "AccountAudit");

        migrationBuilder.DropTable(
            name: "AccountAuditStates",
            schema: "ledger");

        migrationBuilder.DropIndex(
            name: "IX_AccountAudit_AccountHistory",
            schema: "ledger",
            table: "AccountAudit");

        migrationBuilder.DropIndex(
            name: "IX_AccountAudit_CompanyId_WorkingBranchId",
            schema: "ledger",
            table: "AccountAudit");

        migrationBuilder.DropCheckConstraint(
            name: "CK_AccountAudit_Action",
            schema: "ledger",
            table: "AccountAudit");

        migrationBuilder.DropCheckConstraint(
            name: "CK_AccountAudit_Reason",
            schema: "ledger",
            table: "AccountAudit");

        migrationBuilder.DropColumn(
            name: "WorkingBranchId",
            schema: "ledger",
            table: "AccountAudit");

        migrationBuilder.AlterColumn<string>(
            name: "Reason",
            schema: "ledger",
            table: "AccountAudit",
            type: "nvarchar(1000)",
            maxLength: 1000,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(1000)",
            oldMaxLength: 1000,
            oldNullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_AccountAudit_Action",
            schema: "ledger",
            table: "AccountAudit",
            sql: "[Action] IN (1)");

        migrationBuilder.AddCheckConstraint(
            name: "CK_AccountAudit_Reason",
            schema: "ledger",
            table: "AccountAudit",
            sql: "DATALENGTH([Reason]) > 0 AND DATALENGTH([Reason]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Reason]))");
    }
}
