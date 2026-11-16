using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1711

namespace PetraERP.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class PurchaseApprovalPermission : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.InsertData(
            schema: "security",
            table: "Permissions",
            column: "Code",
            value: "Purchases.Invoices.Approve");

        migrationBuilder.Sql(@"
            INSERT INTO [security].[RolePermissions] ([RoleId],[PermissionCode],[CreatedAtUtc])
            SELECT r.[Id],N'Purchases.Invoices.Approve',SYSUTCDATETIME() FROM [security].[Roles] r
            WHERE r.[Code]='ADMINISTRATOR'
            AND NOT EXISTS (SELECT 1 FROM [security].[RolePermissions] rp WHERE rp.[RoleId]=r.[Id] AND rp.[PermissionCode]=N'Purchases.Invoices.Approve');");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DELETE FROM [security].[RolePermissions] WHERE [PermissionCode] = N'Purchases.Invoices.Approve';");
        migrationBuilder.DeleteData(
            schema: "security",
            table: "Permissions",
            keyColumn: "Code",
            keyValue: "Purchases.Invoices.Approve");
    }
}
