using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace PetraERP.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class SalesInvoiceFoundation : Migration
{
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "sales");

            migrationBuilder.CreateTable(
                name: "Invoices",
                schema: "sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatternRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    InvoiceDateUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    PaymentMethod = table.Column<int>(type: "int", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(19,8)", precision: 19, scale: 8, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SubTotal = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    DiscountTotal = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    TaxTotal = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    AdditionsTotal = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    GrandTotal = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReversedByInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReversesInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    PostedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PostedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                    table.CheckConstraint("CK_SalesInvoices_BranchId", "[BranchId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_SalesInvoices_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_SalesInvoices_CustomerId", "[CustomerId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_SalesInvoices_ExchangeRate", "[ExchangeRate] > 0");
                    table.CheckConstraint("CK_SalesInvoices_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_SalesInvoices_InvoiceDateUtc", "DATEPART(TZOFFSET, [InvoiceDateUtc]) = 0");
                    table.CheckConstraint("CK_SalesInvoices_PaymentMethod", "[PaymentMethod] IN (1, 2)");
                    table.CheckConstraint("CK_SalesInvoices_Status", "[Status] IN (0, 1, 2, 3, 4)");
                    table.CheckConstraint("CK_SalesInvoices_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                    table.CheckConstraint("CK_SalesInvoices_WarehouseId", "[WarehouseId] <> '00000000-0000-0000-0000-000000000000'");
                    table.ForeignKey(
                        name: "FK_Invoices_Branches_BranchId",
                        column: x => x.BranchId,
                        principalSchema: "system",
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Invoices_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "system",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Invoices_CostCenters_CostCenterId",
                        column: x => x.CostCenterId,
                        principalSchema: "ledger",
                        principalTable: "CostCenters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Invoices_Parties_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "parties",
                        principalTable: "Parties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Invoices_PatternRevisions_PatternRevisionId",
                        column: x => x.PatternRevisionId,
                        principalSchema: "patterns",
                        principalTable: "PatternRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Invoices_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalSchema: "inventory",
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceLines",
                schema: "sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    DiscountPercentage = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    TaxPercentage = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    PumpCost = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    TransportCost = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    CostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    ConcreteGrade = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceLines", x => x.Id);
                    table.CheckConstraint("CK_SalesInvoiceLines_DiscountAmount", "[DiscountAmount] >= 0");
                    table.CheckConstraint("CK_SalesInvoiceLines_DiscountPercentage", "[DiscountPercentage] >= 0 AND [DiscountPercentage] <= 100");
                    table.CheckConstraint("CK_SalesInvoiceLines_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_SalesInvoiceLines_PumpCost", "[PumpCost] >= 0");
                    table.CheckConstraint("CK_SalesInvoiceLines_Quantity", "[Quantity] > 0");
                    table.CheckConstraint("CK_SalesInvoiceLines_TaxAmount", "[TaxAmount] >= 0");
                    table.CheckConstraint("CK_SalesInvoiceLines_TaxPercentage", "[TaxPercentage] >= 0 AND [TaxPercentage] <= 100");
                    table.CheckConstraint("CK_SalesInvoiceLines_TransportCost", "[TransportCost] >= 0");
                    table.CheckConstraint("CK_SalesInvoiceLines_UnitPrice", "[UnitPrice] >= 0");
                    table.ForeignKey(
                        name: "FK_InvoiceLines_CostCenters_CostCenterId",
                        column: x => x.CostCenterId,
                        principalSchema: "ledger",
                        principalTable: "CostCenters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvoiceLines_Invoices_SalesInvoiceId",
                        column: x => x.SalesInvoiceId,
                        principalSchema: "sales",
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InvoiceLines_Items_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "inventory",
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvoiceLines_UnitsOfMeasure_UnitId",
                        column: x => x.UnitId,
                        principalSchema: "inventory",
                        principalTable: "UnitsOfMeasure",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvoiceLines_Warehouses_WarehouseId",
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
                    "Sales.Invoices.Approve",
                    "Sales.Invoices.Manage",
                    "Sales.Invoices.Post",
                    "Sales.Invoices.Reverse",
                    "Sales.Invoices.View"
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_CostCenterId",
                schema: "sales",
                table: "InvoiceLines",
                column: "CostCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_ItemId",
                schema: "sales",
                table: "InvoiceLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_UnitId",
                schema: "sales",
                table: "InvoiceLines",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_WarehouseId",
                schema: "sales",
                table: "InvoiceLines",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLines_InvoiceId",
                schema: "sales",
                table: "InvoiceLines",
                column: "SalesInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_BranchId",
                schema: "sales",
                table: "Invoices",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_CostCenterId",
                schema: "sales",
                table: "Invoices",
                column: "CostCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_CustomerId",
                schema: "sales",
                table: "Invoices",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_PatternRevisionId",
                schema: "sales",
                table: "Invoices",
                column: "PatternRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_WarehouseId",
                schema: "sales",
                table: "Invoices",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_Company_Customer",
                schema: "sales",
                table: "Invoices",
                columns: new[] { "CompanyId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_Company_Date",
                schema: "sales",
                table: "Invoices",
                columns: new[] { "CompanyId", "InvoiceDateUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_Company_Status",
                schema: "sales",
                table: "Invoices",
                columns: new[] { "CompanyId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UQ_SalesInvoices_Number",
                schema: "sales",
                table: "Invoices",
                columns: new[] { "CompanyId", "BranchId", "InvoiceNumber" },
                unique: true,
                filter: "[InvoiceNumber] IS NOT NULL");

            migrationBuilder.Sql("""
                INSERT INTO [security].[RolePermissions] ([RoleId], [PermissionCode], [CreatedAtUtc])
                SELECT r.[Id], p.[Code], SYSUTCDATETIME()
                FROM [security].[Roles] r CROSS JOIN [security].[Permissions] p
                WHERE r.[Code] = N'ADMINISTRATOR' AND p.[Code] LIKE N'Sales.%'
                AND NOT EXISTS (SELECT 1 FROM [security].[RolePermissions] rp WHERE rp.[RoleId] = r.[Id] AND rp.[PermissionCode] = p.[Code]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove only grants introduced by this migration before deleting their restrictive permission keys.
            migrationBuilder.Sql("""
                DELETE FROM [security].[RolePermissions]
                WHERE [PermissionCode] IN (N'Sales.Invoices.Approve', N'Sales.Invoices.Manage',
                    N'Sales.Invoices.Post', N'Sales.Invoices.Reverse', N'Sales.Invoices.View');
                """);
            migrationBuilder.DropTable(
                name: "InvoiceLines",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "Invoices",
                schema: "sales");

            migrationBuilder.DeleteData(
                schema: "security",
                table: "Permissions",
                keyColumn: "Code",
                keyValue: "Sales.Invoices.Approve");

            migrationBuilder.DeleteData(
                schema: "security",
                table: "Permissions",
                keyColumn: "Code",
                keyValue: "Sales.Invoices.Manage");

            migrationBuilder.DeleteData(
                schema: "security",
                table: "Permissions",
                keyColumn: "Code",
                keyValue: "Sales.Invoices.Post");

            migrationBuilder.DeleteData(
                schema: "security",
                table: "Permissions",
                keyColumn: "Code",
                keyValue: "Sales.Invoices.Reverse");

            migrationBuilder.DeleteData(
                schema: "security",
                table: "Permissions",
                keyColumn: "Code",
                keyValue: "Sales.Invoices.View");
        }
    }
