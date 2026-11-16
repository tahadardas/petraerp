using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1861, IDE0161
namespace PetraERP.Infrastructure.Persistence.Migrations;
    /// <inheritdoc />
    public partial class CompanyPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.EnsureSchema(
                name: "policy");

            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.AddColumn<string>(
                name: "ProvisioningFingerprint",
                schema: "system",
                table: "FiscalDatabases",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);



            migrationBuilder.AddColumn<Guid>(
                name: "SourceFiscalYearId",
                schema: "system",
                table: "FiscalDatabases",
                type: "uniqueidentifier",
                nullable: true);


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
                        name: "FK_CompanyPolicies_DocumentPatterns_DefaultPaymentPatternId",
                        column: x => x.DefaultPaymentPatternId,
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
                        name: "FK_CompanyPolicies_DocumentPatterns_DefaultSalesPatternId",
                        column: x => x.DefaultSalesPatternId,
                        principalSchema: "patterns",
                        principalTable: "DocumentPatterns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyPolicies_FiscalYears_FiscalYearId",
                        column: x => x.FiscalYearId,
                        principalSchema: "ledger",
                        principalTable: "FiscalYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyPolicies_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "security",
                        principalTable: "Users",
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
                        name: "FK_CompanyPolicyAudit_FiscalYears_FiscalYearId",
                        column: x => x.FiscalYearId,
                        principalSchema: "ledger",
                        principalTable: "FiscalYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyPolicyAudit_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalSchema: "security",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentNumberCounters",
                schema: "patterns",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatternId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScopeBranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScopeFiscalYearId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodKey = table.Column<int>(type: "int", nullable: false),
                    LastSequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentNumberCounters", x => new { x.CompanyId, x.PatternId, x.ScopeBranchId, x.ScopeFiscalYearId, x.PeriodKey });
                    table.CheckConstraint("CK_DocumentNumberCounters_Sequence", "[LastSequence] >= 0");
                    table.ForeignKey(
                        name: "FK_DocumentNumberCounters_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "system",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentNumberReservations",
                schema: "patterns",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AllocationKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatternId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScopeBranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScopeFiscalYearId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodKey = table.Column<int>(type: "int", nullable: false),
                    DocumentFamily = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    RequestedManualNumber = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Number = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReservedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentNumberReservations", x => new { x.CompanyId, x.AllocationKey });
                    table.CheckConstraint("CK_DocumentNumberReservations_Number", "DATALENGTH([Number]) > 0 AND DATALENGTH([Number]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Number]))");
                    table.CheckConstraint("CK_DocumentNumberReservations_Sequence", "[Sequence] >= 0");
                    table.CheckConstraint("CK_DocumentNumberReservations_Utc", "DATEPART(TZOFFSET,[ReservedAtUtc])=0");
                    table.ForeignKey(
                        name: "FK_DocumentNumberReservations_Branches_CompanyId_BranchId",
                        columns: x => new { x.CompanyId, x.BranchId },
                        principalSchema: "system",
                        principalTable: "Branches",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentNumberReservations_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "system",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentNumberReservations_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalSchema: "security",
                        principalTable: "Users",
                        principalColumn: "Id",
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
                        name: "FK_NegativeIssueApprovals_Branches_BranchId",
                        column: x => x.BranchId,
                        principalSchema: "system",
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NegativeIssueApprovals_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "system",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NegativeIssueApprovals_FiscalYears_FiscalYearId",
                        column: x => x.FiscalYearId,
                        principalSchema: "ledger",
                        principalTable: "FiscalYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NegativeIssueApprovals_Items_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "inventory",
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NegativeIssueApprovals_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "security",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NegativeIssueApprovals_Users_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalSchema: "security",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NegativeIssueApprovals_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalSchema: "inventory",
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "security",
                table: "Permissions",
                column: "Code",
                values: new object[]
                {
                    "Inventory.Negative.Approve",
                    "Inventory.Negative.RequestApproval",
                    "Patterns.Documents.ManualNumbering",
                    "System.CompanyOptions.Manage",
                });



            migrationBuilder.CreateIndex(
                name: "IX_CompanyPolicies_CreatedByUserId",
                schema: "policy",
                table: "CompanyPolicies",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyPolicies_DefaultPaymentPatternId",
                schema: "policy",
                table: "CompanyPolicies",
                column: "DefaultPaymentPatternId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyPolicies_DefaultPurchasePatternId",
                schema: "policy",
                table: "CompanyPolicies",
                column: "DefaultPurchasePatternId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyPolicies_DefaultReceiptPatternId",
                schema: "policy",
                table: "CompanyPolicies",
                column: "DefaultReceiptPatternId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyPolicies_DefaultSalesPatternId",
                schema: "policy",
                table: "CompanyPolicies",
                column: "DefaultSalesPatternId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyPolicies_FiscalYearId",
                schema: "policy",
                table: "CompanyPolicies",
                column: "FiscalYearId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyPolicyAudit_ActorUserId",
                schema: "audit",
                table: "CompanyPolicyAudit",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyPolicyAudit_CompanyId_OccurredAtUtc",
                schema: "audit",
                table: "CompanyPolicyAudit",
                columns: new[] { "CompanyId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyPolicyAudit_FiscalYearId",
                schema: "audit",
                table: "CompanyPolicyAudit",
                column: "FiscalYearId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentNumberReservations_ActorUserId",
                schema: "patterns",
                table: "DocumentNumberReservations",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentNumberReservations_CompanyId_BranchId",
                schema: "patterns",
                table: "DocumentNumberReservations",
                columns: new[] { "CompanyId", "BranchId" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentNumberReservations_CompanyId_DocumentFamily_ScopeBranchId_ScopeFiscalYearId_Number",
                schema: "patterns",
                table: "DocumentNumberReservations",
                columns: new[] { "CompanyId", "DocumentFamily", "ScopeBranchId", "ScopeFiscalYearId", "Number" },
                unique: true);


            migrationBuilder.CreateIndex(
                name: "IX_NegativeIssueApprovals_BranchId",
                schema: "policy",
                table: "NegativeIssueApprovals",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_NegativeIssueApprovals_CompanyId_BranchId_Status",
                schema: "policy",
                table: "NegativeIssueApprovals",
                columns: new[] { "CompanyId", "BranchId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_NegativeIssueApprovals_CreatedByUserId",
                schema: "policy",
                table: "NegativeIssueApprovals",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_NegativeIssueApprovals_FiscalYearId",
                schema: "policy",
                table: "NegativeIssueApprovals",
                column: "FiscalYearId");

            migrationBuilder.CreateIndex(
                name: "IX_NegativeIssueApprovals_ItemId",
                schema: "policy",
                table: "NegativeIssueApprovals",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_NegativeIssueApprovals_ReviewedByUserId",
                schema: "policy",
                table: "NegativeIssueApprovals",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_NegativeIssueApprovals_WarehouseId",
                schema: "policy",
                table: "NegativeIssueApprovals",
                column: "WarehouseId");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Maintenance repair: the original rollback deleted permissions while
            // bootstrap-created RolePermissions still referenced them (SQL 547).
            // Up/schema history is unchanged. Remove only grants for features being
            // removed by this rollback, before deleting their permission records.
            migrationBuilder.Sql("""
                DELETE FROM [security].[RolePermissions]
                WHERE [PermissionCode] IN (N'Inventory.Negative.Approve', N'Inventory.Negative.RequestApproval',
                    N'Patterns.Documents.ManualNumbering', N'System.CompanyOptions.Manage');
                """);

            migrationBuilder.DropTable(
                name: "CompanyPolicies",
                schema: "policy");

            migrationBuilder.DropTable(
                name: "CompanyPolicyAudit",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "DocumentNumberCounters",
                schema: "patterns");

            migrationBuilder.DropTable(
                name: "DocumentNumberReservations",
                schema: "patterns");


            migrationBuilder.DropTable(
                name: "NegativeIssueApprovals",
                schema: "policy");




            migrationBuilder.DeleteData(
                schema: "security",
                table: "Permissions",
                keyColumn: "Code",
                keyValue: "Inventory.Negative.Approve");

            migrationBuilder.DeleteData(
                schema: "security",
                table: "Permissions",
                keyColumn: "Code",
                keyValue: "Inventory.Negative.RequestApproval");

            migrationBuilder.DeleteData(
                schema: "security",
                table: "Permissions",
                keyColumn: "Code",
                keyValue: "Patterns.Documents.ManualNumbering");

            migrationBuilder.DeleteData(
                schema: "security",
                table: "Permissions",
                keyColumn: "Code",
                keyValue: "System.CompanyOptions.Manage");


            migrationBuilder.DropColumn(
                name: "ProvisioningFingerprint",
                schema: "system",
                table: "FiscalDatabases");



            migrationBuilder.DropColumn(
                name: "SourceFiscalYearId",
                schema: "system",
                table: "FiscalDatabases");


        }
    }

