using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetraERP.Infrastructure.Persistence.Migrations;

/// <summary>
/// Reviewed: adds the append-only <c>ledger.AccountAudit</c> table for deleting unused leaf accounts (PT-030). The snapshot keeps
/// the deleted account's identity, so <c>AccountId</c>/<c>ParentId</c> have no foreign key. No existing table or data changes.
/// </summary>
public partial class ChartOfAccountsDeletionAudit : Migration
{
    private static readonly string[] CompanyAccountColumns = ["CompanyId", "AccountId"];
    private static readonly string[] CompanyTimeColumns = ["CompanyId", "OccurredAtUtc"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AccountAudit",
            schema: "ledger",
            columns: table => new
            {
                EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Action = table.Column<byte>(type: "tinyint", nullable: false),
                Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                NameArabic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                NameEnglish = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                AccountType = table.Column<byte>(type: "tinyint", nullable: false),
                ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AccountAudit", x => x.EventId);
                table.CheckConstraint("CK_AccountAudit_AccountId", "[AccountId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_AccountAudit_AccountType", "[AccountType] BETWEEN 1 AND 5");
                table.CheckConstraint("CK_AccountAudit_Action", "[Action] IN (1)");
                table.CheckConstraint("CK_AccountAudit_ActorUserId", "[ActorUserId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_AccountAudit_CorrelationId", "[CorrelationId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_AccountAudit_EventId", "[EventId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_AccountAudit_Reason", "DATALENGTH([Reason]) > 0 AND DATALENGTH([Reason]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Reason]))");
                table.CheckConstraint("CK_AccountAudit_Utc", "DATEPART(TZOFFSET, [OccurredAtUtc]) = 0 AND DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_AccountAudit_Companies",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AccountAudit_CompanyAccount",
            schema: "ledger",
            table: "AccountAudit",
            columns: CompanyAccountColumns);

        migrationBuilder.CreateIndex(
            name: "IX_AccountAudit_CompanyTime",
            schema: "ledger",
            table: "AccountAudit",
            columns: CompanyTimeColumns);

        // Reviewed addition: the deletion evidence is append-only at the database level (defense in depth).
        migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [ledger].[TR_AccountAudit_AppendOnly]
                ON [ledger].[AccountAudit] INSTEAD OF UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51011, ''Account audit events are append-only and cannot be updated or deleted.'', 1;
                END;');
                """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS [ledger].[TR_AccountAudit_AppendOnly];");
        migrationBuilder.DropTable(
            name: "AccountAudit",
            schema: "ledger");
    }
}
