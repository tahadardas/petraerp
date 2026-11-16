using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace PetraERP.Infrastructure.Persistence.Migrations;

[DbContext(typeof(PetraDbContext))]
[Migration("20261024090000_DatabaseManagementPermission")]
public sealed partial class DatabaseManagementAuthorization : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.Sql("""
            IF NOT EXISTS (SELECT 1 FROM [security].[Permissions] WHERE [Code] = N'System.Database.Manage')
                INSERT INTO [security].[Permissions] ([Code]) VALUES (N'System.Database.Manage');
            INSERT INTO [security].[RolePermissions] ([RoleId], [PermissionCode], [CreatedAtUtc])
            SELECT r.[Id], N'System.Database.Manage', SYSUTCDATETIME()
            FROM [security].[Roles] r
            WHERE r.[Code] = N'ADMINISTRATOR'
              AND NOT EXISTS (SELECT 1 FROM [security].[RolePermissions] rp
                              WHERE rp.[RoleId] = r.[Id] AND rp.[PermissionCode] = N'System.Database.Manage');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.Sql("""
            DELETE FROM [security].[RolePermissions] WHERE [PermissionCode] = N'System.Database.Manage';
            DELETE FROM [security].[Permissions] WHERE [Code] = N'System.Database.Manage';
            """);
    }
}
