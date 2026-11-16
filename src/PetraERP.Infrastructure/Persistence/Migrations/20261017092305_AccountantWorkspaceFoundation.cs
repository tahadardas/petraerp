using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetraERP.Infrastructure.Persistence.Migrations;
/// <inheritdoc />
public partial class AccountantWorkspaceFoundation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "workspace");

        migrationBuilder.CreateTable(
            name: "WorkspacePreferences",
            schema: "workspace",
            columns: table => new
            {
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Document = table.Column<string>(type: "nvarchar(max)", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WorkspacePreferences", x => x.UserId);
                table.CheckConstraint("CK_WorkspacePreferences_Document", "ISJSON([Document]) = 1 AND DATALENGTH([Document]) <= 65536");
                table.CheckConstraint("CK_WorkspacePreferences_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_WorkspacePreferences_Users_UserId",
                    column: x => x.UserId,
                    principalSchema: "security",
                    principalTable: "Users",
                    principalColumn: "Id");
            });

        migrationBuilder.InsertData(
            schema: "security",
            table: "Permissions",
            column: "Code",
            values: new object[]
            {
                "Ledger.Accounts.View",
                "Ledger.Journals.View"
            });
        // Extend the built-in installation administrator only; other roles require explicit grants.
        migrationBuilder.Sql("""
            INSERT INTO [security].[RolePermissions] ([RoleId], [PermissionCode], [CreatedAtUtc])
            SELECT r.[Id], p.[Code], SYSUTCDATETIME()
            FROM [security].[Roles] r CROSS JOIN [security].[Permissions] p
            WHERE r.[Code] = N'ADMINISTRATOR' AND p.[Code] IN (N'Ledger.Accounts.View', N'Ledger.Journals.View')
            AND NOT EXISTS (SELECT 1 FROM [security].[RolePermissions] rp WHERE rp.[RoleId] = r.[Id] AND rp.[PermissionCode] = p.[Code]);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "WorkspacePreferences",
            schema: "workspace");

        migrationBuilder.Sql("DELETE FROM [security].[RolePermissions] WHERE [PermissionCode] IN (N'Ledger.Accounts.View', N'Ledger.Journals.View');");

        migrationBuilder.DeleteData(
            schema: "security",
            table: "Permissions",
            keyColumn: "Code",
            keyValue: "Ledger.Accounts.View");

        migrationBuilder.DeleteData(
            schema: "security",
            table: "Permissions",
            keyColumn: "Code",
            keyValue: "Ledger.Journals.View");
    }
}
