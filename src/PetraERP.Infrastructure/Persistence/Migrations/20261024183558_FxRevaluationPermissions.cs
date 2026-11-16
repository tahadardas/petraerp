using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetraERP.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class FxRevaluationPermissions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        // Seed the two FX-revaluation permission codes (model HasData). RolePermissions reference Permissions by FK,
        // so these rows must exist before any grant.
        migrationBuilder.InsertData(
            schema: "security",
            table: "Permissions",
            column: "Code",
            values: new object[]
            {
                "Currencies.FxRevaluation.Execute",
                "Currencies.FxRevaluation.View"
            });

        // Grant both to an already-provisioned ADMINISTRATOR role (existing databases). A fresh installation grants them
        // through the bootstrapper, which assigns the full permission catalog; the NOT EXISTS keeps this idempotent.
        migrationBuilder.Sql("""
            INSERT INTO [security].[RolePermissions] ([RoleId], [PermissionCode], [CreatedAtUtc])
            SELECT r.[Id], p.[Code], SYSUTCDATETIME()
            FROM [security].[Roles] r
            CROSS JOIN (VALUES (N'Currencies.FxRevaluation.View'), (N'Currencies.FxRevaluation.Execute')) AS p([Code])
            WHERE r.[Code] = N'ADMINISTRATOR'
              AND NOT EXISTS (SELECT 1 FROM [security].[RolePermissions] rp
                              WHERE rp.[RoleId] = r.[Id] AND rp.[PermissionCode] = p.[Code]);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.Sql("""
            DELETE FROM [security].[RolePermissions]
            WHERE [PermissionCode] IN (N'Currencies.FxRevaluation.View', N'Currencies.FxRevaluation.Execute');
            """);

        migrationBuilder.DeleteData(
            schema: "security",
            table: "Permissions",
            keyColumn: "Code",
            keyValue: "Currencies.FxRevaluation.Execute");

        migrationBuilder.DeleteData(
            schema: "security",
            table: "Permissions",
            keyColumn: "Code",
            keyValue: "Currencies.FxRevaluation.View");
    }
}
