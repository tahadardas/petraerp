using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetraERP.Infrastructure.Persistence.Migrations;

/// <summary>Upgrades an existing installation administrator without granting ordinary roles new privileges.</summary>
public partial class PhaseTwoAdministratorPermissions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.Sql("""
            INSERT INTO [security].[RolePermissions] ([RoleId], [PermissionCode], [CreatedAtUtc])
            SELECT r.[Id], p.[Code], SYSUTCDATETIME()
            FROM [security].[Roles] r
            CROSS JOIN [security].[Permissions] p
            WHERE r.[Code] = N'ADMINISTRATOR'
              AND p.[Code] IN (N'Inventory.Negative.Approve', N'Inventory.Negative.RequestApproval',
                  N'Patterns.Documents.ManualNumbering', N'System.CompanyOptions.Manage')
              AND NOT EXISTS (SELECT 1 FROM [security].[RolePermissions] rp
                  WHERE rp.[RoleId] = r.[Id] AND rp.[PermissionCode] = p.[Code]);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        // Pre-existing administrator grants cannot be distinguished from upgraded grants.
        // Keep them when rolling back only this data repair. CompanyPolicies.Down removes
        // these grants before removing the corresponding feature permissions themselves.
    }
}
