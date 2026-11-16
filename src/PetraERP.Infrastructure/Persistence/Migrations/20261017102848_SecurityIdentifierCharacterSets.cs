using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetraERP.Infrastructure.Persistence.Migrations;
/// <inheritdoc />
public partial class SecurityIdentifierCharacterSets : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_Users_UserName",
            schema: "security",
            table: "Users");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Roles_Code",
            schema: "security",
            table: "Roles");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Users_UserName",
            schema: "security",
            table: "Users",
            sql: "LEN([UserName]) BETWEEN 3 AND 64 AND [UserName] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^-A-Za-z0-9._]%'");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Roles_Code",
            schema: "security",
            table: "Roles",
            sql: "LEN([Code]) BETWEEN 2 AND 64 AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^-A-Z0-9._]%'");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [security].[Users] WHERE CHARINDEX(N'_', [UserName]) > 0 OR CHARINDEX(N'-', [UserName]) > 0)
                OR EXISTS (SELECT 1 FROM [security].[Roles] WHERE CHARINDEX(N'_', [Code]) > 0 OR CHARINDEX(N'-', [Code]) > 0)
                THROW 51004, 'Downgrade blocked: identifiers using underscore or hyphen require the current security schema. No data was changed.', 1;
            """);

        migrationBuilder.DropCheckConstraint(
            name: "CK_Users_UserName",
            schema: "security",
            table: "Users");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Roles_Code",
            schema: "security",
            table: "Roles");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Users_UserName",
            schema: "security",
            table: "Users",
            sql: "LEN([UserName]) BETWEEN 3 AND 64 AND [UserName] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Za-z0-9._-]%'");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Roles_Code",
            schema: "security",
            table: "Roles",
            sql: "LEN([Code]) BETWEEN 2 AND 64 AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9._-]%'");
    }
}
