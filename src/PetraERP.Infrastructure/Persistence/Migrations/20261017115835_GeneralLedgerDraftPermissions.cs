using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetraERP.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class GeneralLedgerDraftPermissions : Migration
{
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "security",
                table: "Permissions",
                column: "Code",
                values: new object[]
                {
                    "Ledger.Journals.Approve",
                    "Ledger.Journals.Manage"
                });

            // Extend the built-in installation administrator only; other roles require explicit grants.
            migrationBuilder.Sql("""
                INSERT INTO [security].[RolePermissions] ([RoleId], [PermissionCode], [CreatedAtUtc])
                SELECT r.[Id], p.[Code], SYSUTCDATETIME()
                FROM [security].[Roles] r CROSS JOIN [security].[Permissions] p
                WHERE r.[Code] = N'ADMINISTRATOR' AND p.[Code] IN (N'Ledger.Journals.Approve', N'Ledger.Journals.Manage')
                AND NOT EXISTS (SELECT 1 FROM [security].[RolePermissions] rp WHERE rp.[RoleId] = r.[Id] AND rp.[PermissionCode] = p.[Code]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM [security].[RolePermissions] WHERE [PermissionCode] IN (N'Ledger.Journals.Approve', N'Ledger.Journals.Manage');");

            migrationBuilder.DeleteData(
                schema: "security",
                table: "Permissions",
                keyColumn: "Code",
                keyValue: "Ledger.Journals.Approve");

            migrationBuilder.DeleteData(
                schema: "security",
                table: "Permissions",
                keyColumn: "Code",
                keyValue: "Ledger.Journals.Manage");
        }
    }
