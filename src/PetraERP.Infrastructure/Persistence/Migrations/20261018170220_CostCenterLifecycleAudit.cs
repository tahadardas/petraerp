using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetraERP.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class CostCenterLifecycleAudit : Migration
{
    private static readonly string[] CompanyIdentity = ["CompanyId", "Id"];
    private static readonly string[] WorkingBranch = ["CompanyId", "WorkingBranchId"];
    private static readonly string[] HistoryOrder = ["CompanyId", "CostCenterId", "OccurredAtUtc", "EventId"];
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "CostCenterAudit",
            schema: "ledger",
            columns: table => new
            {
                EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Action = table.Column<byte>(type: "tinyint", nullable: false),
                ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkingBranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CostCenterAudit", x => x.EventId);
                table.CheckConstraint("CK_CostCenterAudit_Action", "[Action] IN (1,2,3,4)");
                table.CheckConstraint("CK_CostCenterAudit_Ids", "[EventId] <> '00000000-0000-0000-0000-000000000000' AND [CostCenterId] <> '00000000-0000-0000-0000-000000000000' AND [CorrelationId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_CostCenterAudit_Utc", "DATEPART(TZOFFSET,[OccurredAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_CostCenterAudit_Branches_CompanyId_WorkingBranchId",
                    columns: x => new { x.CompanyId, x.WorkingBranchId },
                    principalSchema: "system",
                    principalTable: "Branches",
                    principalColumns: CompanyIdentity,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CostCenterAudit_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CostCenterAudit_Users_ActorUserId",
                    column: x => x.ActorUserId,
                    principalSchema: "security",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "CostCenterAuditStates",
            schema: "ledger",
            columns: table => new
            {
                EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Stage = table.Column<byte>(type: "tinyint", nullable: false),
                Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                NameArabic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                NameEnglish = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                IsActive = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CostCenterAuditStates", x => new { x.EventId, x.Stage });
                table.CheckConstraint("CK_CostCenterAuditStates_Code", "DATALENGTH([Code]) > 0 AND DATALENGTH([Code]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Code]))");
                table.CheckConstraint("CK_CostCenterAuditStates_Names", "(DATALENGTH([NameArabic]) > 0 AND DATALENGTH([NameArabic]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [NameArabic]))) AND ([NameEnglish] IS NULL OR (DATALENGTH([NameEnglish]) > 0 AND DATALENGTH([NameEnglish]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [NameEnglish]))))");
                table.CheckConstraint("CK_CostCenterAuditStates_Stage", "[Stage] IN (1,2)");
                table.ForeignKey(
                    name: "FK_CostCenterAuditStates_CostCenterAudit_EventId",
                    column: x => x.EventId,
                    principalSchema: "ledger",
                    principalTable: "CostCenterAudit",
                    principalColumn: "EventId",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_CostCenterAudit_ActorUserId",
            schema: "ledger",
            table: "CostCenterAudit",
            column: "ActorUserId");

        migrationBuilder.CreateIndex(
            name: "IX_CostCenterAudit_CompanyId_WorkingBranchId",
            schema: "ledger",
            table: "CostCenterAudit",
            columns: WorkingBranch);

        migrationBuilder.CreateIndex(
            name: "IX_CostCenterAudit_History",
            schema: "ledger",
            table: "CostCenterAudit",
            columns: HistoryOrder);
        migrationBuilder.Sql("""
            EXEC(N'CREATE TRIGGER [ledger].[TR_CostCenterAudit_AppendOnly]
            ON [ledger].[CostCenterAudit] INSTEAD OF UPDATE, DELETE AS
            BEGIN
                SET NOCOUNT ON;
                THROW 51014, ''Cost-center audit is append-only.'', 1;
            END;');
            EXEC(N'CREATE TRIGGER [ledger].[TR_CostCenterAuditStates_AppendOnly]
            ON [ledger].[CostCenterAuditStates] INSTEAD OF UPDATE, DELETE AS
            BEGIN
                SET NOCOUNT ON;
                THROW 51015, ''Cost-center audit states are append-only.'', 1;
            END;');
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Empty schema is reversible; recorded evidence requires a reviewed backup, never silent deletion.
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [ledger].[CostCenterAudit]) OR EXISTS (SELECT 1 FROM [ledger].[CostCenterAuditStates])
                THROW 51016, 'Cost-center audit has recorded data; restore a reviewed backup instead of discarding audit history.', 1;
            DROP TRIGGER IF EXISTS [ledger].[TR_CostCenterAuditStates_AppendOnly];
            DROP TRIGGER IF EXISTS [ledger].[TR_CostCenterAudit_AppendOnly];
            """);
        migrationBuilder.DropTable(
            name: "CostCenterAuditStates",
            schema: "ledger");

        migrationBuilder.DropTable(
            name: "CostCenterAudit",
            schema: "ledger");
    }
}
