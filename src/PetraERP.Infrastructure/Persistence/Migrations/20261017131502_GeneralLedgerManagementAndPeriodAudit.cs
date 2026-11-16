using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetraERP.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class GeneralLedgerManagementAndPeriodAudit : Migration
{
    private static readonly string[] PeriodAuditIndexColumns = ["CompanyId", "PeriodId", "OccurredAtUtc"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PeriodAudit",
            schema: "ledger",
            columns: table => new
            {
                EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                FiscalYearId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PreviousState = table.Column<byte>(type: "tinyint", nullable: false),
                NewState = table.Column<byte>(type: "tinyint", nullable: false),
                ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PeriodAudit", x => x.EventId);
                table.CheckConstraint("CK_PeriodAudit_ActorUserId", "[ActorUserId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_PeriodAudit_CorrelationId", "[CorrelationId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_PeriodAudit_EventId", "[EventId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_PeriodAudit_Reason", "DATALENGTH([Reason]) > 0 AND DATALENGTH([Reason]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Reason]))");
                table.CheckConstraint("CK_PeriodAudit_States", "[PreviousState] IN (0, 1, 2) AND [NewState] IN (0, 1, 2) AND [PreviousState] <> [NewState]");
                table.CheckConstraint("CK_PeriodAudit_Utc", "DATEPART(TZOFFSET, [OccurredAtUtc]) = 0 AND DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_PeriodAudit_AccountingPeriods",
                    column: x => x.PeriodId,
                    principalSchema: "ledger",
                    principalTable: "AccountingPeriods",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PeriodAudit_Companies",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PeriodAudit_FiscalYears",
                    column: x => x.FiscalYearId,
                    principalSchema: "ledger",
                    principalTable: "FiscalYears",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.InsertData(
            schema: "security",
            table: "Permissions",
            column: "Code",
            values: new object[]
            {
                "Ledger.Accounts.Manage",
                "Ledger.CostCenters.Manage",
                "Ledger.CostCenters.View",
                "Ledger.Periods.Manage",
                "Ledger.Periods.Reopen",
                "Ledger.Periods.View",
                "Ledger.Reports.View"
            });

        // Extend the built-in installation administrator only; other roles require explicit grants.
        migrationBuilder.Sql("""
            INSERT INTO [security].[RolePermissions] ([RoleId], [PermissionCode], [CreatedAtUtc])
            SELECT r.[Id], p.[Code], SYSUTCDATETIME()
            FROM [security].[Roles] r CROSS JOIN [security].[Permissions] p
            WHERE r.[Code] = N'ADMINISTRATOR' AND p.[Code] IN (N'Ledger.Accounts.Manage', N'Ledger.CostCenters.Manage', N'Ledger.CostCenters.View', N'Ledger.Periods.Manage', N'Ledger.Periods.Reopen', N'Ledger.Periods.View', N'Ledger.Reports.View')
            AND NOT EXISTS (SELECT 1 FROM [security].[RolePermissions] rp WHERE rp.[RoleId] = r.[Id] AND rp.[PermissionCode] = p.[Code]);
            """);

        // The cost-center lookup previously required Ledger.Accounts.View; keep existing roles working.
        migrationBuilder.Sql("""
            INSERT INTO [security].[RolePermissions] ([RoleId], [PermissionCode], [CreatedAtUtc])
            SELECT rp.[RoleId], N'Ledger.CostCenters.View', SYSUTCDATETIME()
            FROM [security].[RolePermissions] rp
            WHERE rp.[PermissionCode] = N'Ledger.Accounts.View'
            AND NOT EXISTS (SELECT 1 FROM [security].[RolePermissions] x WHERE x.[RoleId] = rp.[RoleId] AND x.[PermissionCode] = N'Ledger.CostCenters.View');
            """);

        migrationBuilder.CreateIndex(
            name: "IX_PeriodAudit_CompanyPeriodTime",
            schema: "ledger",
            table: "PeriodAudit",
            columns: PeriodAuditIndexColumns);

        migrationBuilder.CreateIndex(
            name: "IX_PeriodAudit_FiscalYearId",
            schema: "ledger",
            table: "PeriodAudit",
            column: "FiscalYearId");

        migrationBuilder.CreateIndex(
            name: "IX_PeriodAudit_PeriodId",
            schema: "ledger",
            table: "PeriodAudit",
            column: "PeriodId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "PeriodAudit",
            schema: "ledger");

        migrationBuilder.Sql("DELETE FROM [security].[RolePermissions] WHERE [PermissionCode] IN (N'Ledger.Accounts.Manage', N'Ledger.CostCenters.Manage', N'Ledger.CostCenters.View', N'Ledger.Periods.Manage', N'Ledger.Periods.Reopen', N'Ledger.Periods.View', N'Ledger.Reports.View');");

        migrationBuilder.DeleteData(
            schema: "security",
            table: "Permissions",
            keyColumn: "Code",
            keyValue: "Ledger.Accounts.Manage");

        migrationBuilder.DeleteData(
            schema: "security",
            table: "Permissions",
            keyColumn: "Code",
            keyValue: "Ledger.CostCenters.Manage");

        migrationBuilder.DeleteData(
            schema: "security",
            table: "Permissions",
            keyColumn: "Code",
            keyValue: "Ledger.CostCenters.View");

        migrationBuilder.DeleteData(
            schema: "security",
            table: "Permissions",
            keyColumn: "Code",
            keyValue: "Ledger.Periods.Manage");

        migrationBuilder.DeleteData(
            schema: "security",
            table: "Permissions",
            keyColumn: "Code",
            keyValue: "Ledger.Periods.Reopen");

        migrationBuilder.DeleteData(
            schema: "security",
            table: "Permissions",
            keyColumn: "Code",
            keyValue: "Ledger.Periods.View");

        migrationBuilder.DeleteData(
            schema: "security",
            table: "Permissions",
            keyColumn: "Code",
            keyValue: "Ledger.Reports.View");
    }
}
