using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetraERP.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class ModuleActionAuditEvent : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_SecurityAuditEvents_EventType",
            schema: "security",
            table: "SecurityAuditEvents");

        migrationBuilder.AddCheckConstraint(
            name: "CK_SecurityAuditEvents_EventType",
            schema: "security",
            table: "SecurityAuditEvents",
            sql: "[EventType] BETWEEN 1 AND 26");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_SecurityAuditEvents_EventType",
            schema: "security",
            table: "SecurityAuditEvents");

        migrationBuilder.AddCheckConstraint(
            name: "CK_SecurityAuditEvents_EventType",
            schema: "security",
            table: "SecurityAuditEvents",
            sql: "[EventType] BETWEEN 1 AND 25");
    }
}
