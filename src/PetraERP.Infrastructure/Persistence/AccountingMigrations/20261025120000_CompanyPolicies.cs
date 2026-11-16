using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetraERP.Infrastructure.Persistence.AccountingMigrations;

/// <inheritdoc />
public partial class CompanyPolicies : Migration
{
    private static readonly string[] CompanyPrincipalColumns = ["Id"];
    private static readonly string[] CompanyBranchPrincipalColumns = ["CompanyId", "Id"];
    private static readonly string[] CompanyFiscalYearPrincipalColumns = ["CompanyId", "Id"];
    private static readonly string[] ActorPrincipalColumns = ["CompanyId", "Id"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "policy");
        migrationBuilder.CreateTable(
            name: "CompanyPolicies",
            schema: "policy",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                FiscalYearId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                NegativeIssuePolicy = table.Column<byte>(type: "tinyint", nullable: false),
                RequireDocumentApproval = table.Column<bool>(type: "bit", nullable: false),
                RequireProfitLossCostCenter = table.Column<bool>(type: "bit", nullable: false),
                AllowCreditSales = table.Column<bool>(type: "bit", nullable: false),
                AllowCreditPurchases = table.Column<bool>(type: "bit", nullable: false),
                AllowManualNumbering = table.Column<bool>(type: "bit", nullable: false),
                AllowPatternNumberingOverrides = table.Column<bool>(type: "bit", nullable: false),
                DefaultNumberingScope = table.Column<int>(type: "int", nullable: false),
                InitialNumberSequence = table.Column<long>(type: "bigint", nullable: false),
                DefaultSalesPatternId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                DefaultPurchasePatternId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                DefaultReceiptPatternId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                DefaultPaymentPatternId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CompanyPolicies", x => x.CompanyId);
                table.CheckConstraint("CK_CompanyPolicies_NegativeMode", "[NegativeIssuePolicy] IN (0,1,2)");
                table.CheckConstraint("CK_CompanyPolicies_Numbering", "[DefaultNumberingScope] IN (1,2,3,4) AND [InitialNumberSequence] BETWEEN 1 AND 999999999999");
                table.CheckConstraint("CK_CompanyPolicies_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_CompanyPolicies_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CompanyPolicies_FiscalYears_CompanyId_FiscalYearId",
                    columns: x => new { x.CompanyId, x.FiscalYearId },
                    principalSchema: "ledger",
                    principalTable: "FiscalYears",
                    principalColumns: CompanyFiscalYearPrincipalColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CompanyPolicies_ActorReferences_CompanyId_CreatedByUserId",
                    columns: x => new { x.CompanyId, x.CreatedByUserId },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: ActorPrincipalColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CompanyPolicies_DocumentPatterns_DefaultSalesPatternId",
                    column: x => x.DefaultSalesPatternId,
                    principalSchema: "patterns",
                    principalTable: "DocumentPatterns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CompanyPolicies_DocumentPatterns_DefaultPurchasePatternId",
                    column: x => x.DefaultPurchasePatternId,
                    principalSchema: "patterns",
                    principalTable: "DocumentPatterns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CompanyPolicies_DocumentPatterns_DefaultReceiptPatternId",
                    column: x => x.DefaultReceiptPatternId,
                    principalSchema: "patterns",
                    principalTable: "DocumentPatterns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CompanyPolicies_DocumentPatterns_DefaultPaymentPatternId",
                    column: x => x.DefaultPaymentPatternId,
                    principalSchema: "patterns",
                    principalTable: "DocumentPatterns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "CompanyPolicyAudit",
            schema: "audit",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                FiscalYearId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EventType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                BeforeJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                AfterJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CompanyPolicyAudit", x => x.Id);
                table.CheckConstraint("CK_CompanyPolicyAudit_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_CompanyPolicyAudit_Json", "ISJSON([AfterJson])=1 AND ([BeforeJson] IS NULL OR ISJSON([BeforeJson])=1)");
                table.CheckConstraint("CK_CompanyPolicyAudit_Utc", "DATEPART(TZOFFSET,[OccurredAtUtc])=0");
                table.ForeignKey(
                    name: "FK_CompanyPolicyAudit_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CompanyPolicyAudit_FiscalYears_CompanyId_FiscalYearId",
                    columns: x => new { x.CompanyId, x.FiscalYearId },
                    principalSchema: "ledger",
                    principalTable: "FiscalYears",
                    principalColumns: CompanyFiscalYearPrincipalColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CompanyPolicyAudit_ActorReferences_CompanyId_ActorUserId",
                    columns: x => new { x.CompanyId, x.ActorUserId },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: ActorPrincipalColumns,
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "NegativeIssueApprovals",
            schema: "policy",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                FiscalYearId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Quantity = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NegativeIssueApprovals", x => x.Id);
                table.CheckConstraint("CK_NegativeIssueApprovals_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_NegativeIssueApprovals_Quantity", "[Quantity]>0");
                table.CheckConstraint("CK_NegativeIssueApprovals_Status", "[Status] IN (N'Pending',N'ApprovedPolicyOnly',N'Rejected')");
                table.CheckConstraint("CK_NegativeIssueApprovals_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_NegativeIssueApprovals_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_NegativeIssueApprovals_Branches_CompanyId_BranchId",
                    columns: x => new { x.CompanyId, x.BranchId },
                    principalSchema: "system",
                    principalTable: "Branches",
                    principalColumns: CompanyBranchPrincipalColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_NegativeIssueApprovals_FiscalYears_CompanyId_FiscalYearId",
                    columns: x => new { x.CompanyId, x.FiscalYearId },
                    principalSchema: "ledger",
                    principalTable: "FiscalYears",
                    principalColumns: CompanyFiscalYearPrincipalColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_NegativeIssueApprovals_Items_ItemId",
                    column: x => x.ItemId,
                    principalSchema: "inventory",
                    principalTable: "Items",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_NegativeIssueApprovals_Warehouses_WarehouseId",
                    column: x => x.WarehouseId,
                    principalSchema: "inventory",
                    principalTable: "Warehouses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_NegativeIssueApprovals_ActorReferences_CompanyId_CreatedByUserId",
                    columns: x => new { x.CompanyId, x.CreatedByUserId },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: ActorPrincipalColumns,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_NegativeIssueApprovals_ActorReferences_CompanyId_ReviewedByUserId",
                    columns: x => new { x.CompanyId, x.ReviewedByUserId },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: ActorPrincipalColumns,
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_CompanyPolicyAudit_CompanyId_OccurredAtUtc",
            schema: "audit",
            table: "CompanyPolicyAudit",
            columns: ["CompanyId", "OccurredAtUtc"]);

        migrationBuilder.CreateIndex(
            name: "IX_NegativeIssueApprovals_CompanyId_BranchId_Status",
            schema: "policy",
            table: "NegativeIssueApprovals",
            columns: ["CompanyId", "BranchId", "Status"]);

        migrationBuilder.Sql("""
            EXEC(N'CREATE TRIGGER [audit].[TR_CompanyPolicyAudit_Immutable] ON [audit].[CompanyPolicyAudit]
            AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON;
                THROW 51020, ''Accounting audit and posted movement history is immutable.'', 1;
            END;');
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS [audit].[TR_CompanyPolicyAudit_Immutable];");

        migrationBuilder.DropTable(
            name: "NegativeIssueApprovals",
            schema: "policy");

        migrationBuilder.DropTable(
            name: "CompanyPolicyAudit",
            schema: "audit");

        migrationBuilder.DropTable(
            name: "CompanyPolicies",
            schema: "policy");
    }
}
