using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace PetraERP.Infrastructure.Persistence.AccountingMigrations;
/// <inheritdoc />
public partial class DocumentRequestBindings : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "DocumentRequestBindings",
            schema: "audit",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DocumentKind = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RequestHashHex = table.Column<string>(type: "char(64)", nullable: false),
                RegisteredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DocumentRequestBindings", x => new { x.CompanyId, x.BranchId, x.DocumentKind, x.IdempotencyKey });
                table.CheckConstraint("CK_DocumentRequestBindings_Hash", "LEN([RequestHashHex])=64");
                table.CheckConstraint("CK_DocumentRequestBindings_Key", "LEN(LTRIM(RTRIM([IdempotencyKey])))>0");
                table.ForeignKey(
                    name: "FK_DocumentRequestBindings_ActorReferences_CompanyId_ActorUserId",
                    columns: x => new { x.CompanyId, x.ActorUserId },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_DocumentRequestBindings_Branches_CompanyId_BranchId",
                    columns: x => new { x.CompanyId, x.BranchId },
                    principalSchema: "system",
                    principalTable: "Branches",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_DocumentRequestBindings_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_DocumentRequestBindings_CompanyId_ActorUserId",
            schema: "audit",
            table: "DocumentRequestBindings",
            columns: new[] { "CompanyId", "ActorUserId" });

        migrationBuilder.CreateIndex(
            name: "IX_DocumentRequestBindings_CompanyId_BranchId_DocumentId",
            schema: "audit",
            table: "DocumentRequestBindings",
            columns: new[] { "CompanyId", "BranchId", "DocumentId" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "DocumentRequestBindings",
            schema: "audit");
    }
}
