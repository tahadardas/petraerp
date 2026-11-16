using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace PetraERP.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class PurchaseInvoices : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "purchases");

        migrationBuilder.CreateTable(
            name: "Invoices",
            schema: "purchases",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PatternRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                InvoiceNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                SupplierReference = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                InvoiceDateUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                PaymentMethod = table.Column<int>(type: "int", nullable: false),
                CurrencyCode = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                ExchangeRate = table.Column<decimal>(type: "decimal(19,8)", precision: 19, scale: 8, nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                SubTotal = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                DiscountTotal = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                TaxTotal = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                GrandTotal = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                JournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ReturnOfInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                PostingKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2"),
                CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PostedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                PostedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Invoices", x => x.Id);
                table.CheckConstraint("CK_PurchaseInvoices_ExchangeRate", "[ExchangeRate] > 0");
                table.CheckConstraint("CK_PurchaseInvoices_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_PurchaseInvoices_InvoiceDateUtc", "DATEPART(TZOFFSET, [InvoiceDateUtc]) = 0");
                table.CheckConstraint("CK_PurchaseInvoices_PaymentMethod", "[PaymentMethod] IN (1, 2)");
                table.CheckConstraint("CK_PurchaseInvoices_Posted", "[Status] = 0 OR ([JournalEntryId] IS NOT NULL AND [InvoiceNumber] IS NOT NULL AND [PostingKey] IS NOT NULL AND [PostedAtUtc] IS NOT NULL)");
                table.CheckConstraint("CK_PurchaseInvoices_Return", "[ReturnOfInvoiceId] IS NULL OR ([Status] = 2 AND [ReturnOfInvoiceId] <> [Id])");
                table.CheckConstraint("CK_PurchaseInvoices_Status", "[Status] IN (0, 2)");
                table.CheckConstraint("CK_PurchaseInvoices_Totals", "[SubTotal] >= 0 AND [DiscountTotal] >= 0 AND [TaxTotal] >= 0 AND [GrandTotal] = [SubTotal] - [DiscountTotal] + [TaxTotal]");
                table.CheckConstraint("CK_PurchaseInvoices_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
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
                    name: "FK_Invoices_Invoices_ReturnOfInvoiceId",
                    column: x => x.ReturnOfInvoiceId,
                    principalSchema: "purchases",
                    principalTable: "Invoices",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Invoices_JournalEntries_JournalEntryId",
                    column: x => x.JournalEntryId,
                    principalSchema: "ledger",
                    principalTable: "JournalEntries",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Invoices_Parties_SupplierId",
                    column: x => x.SupplierId,
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
            schema: "purchases",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PurchaseInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Quantity = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                UnitCost = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                DiscountAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                TaxAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                LineTotal = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                CostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                DisplayOrder = table.Column<int>(type: "int", nullable: false),
                SourceLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_InvoiceLines", x => x.Id);
                table.CheckConstraint("CK_PurchaseInvoiceLines_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_PurchaseInvoiceLines_Money", "[UnitCost] >= 0 AND [DiscountAmount] >= 0 AND [TaxAmount] >= 0 AND [LineTotal] >= 0");
                table.CheckConstraint("CK_PurchaseInvoiceLines_Quantity", "[Quantity] > 0");
                table.CheckConstraint("CK_PurchaseInvoiceLines_SourceLineId", "[SourceLineId] IS NULL OR [SourceLineId] <> [Id]");
                table.ForeignKey(
                    name: "FK_InvoiceLines_CostCenters_CostCenterId",
                    column: x => x.CostCenterId,
                    principalSchema: "ledger",
                    principalTable: "CostCenters",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_InvoiceLines_InvoiceLines_SourceLineId",
                    column: x => x.SourceLineId,
                    principalSchema: "purchases",
                    principalTable: "InvoiceLines",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_InvoiceLines_Invoices_PurchaseInvoiceId",
                    column: x => x.PurchaseInvoiceId,
                    principalSchema: "purchases",
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
            });

        migrationBuilder.InsertData(
            schema: "security",
            table: "Permissions",
            column: "Code",
            values: new object[]
            {
                "Purchases.Invoices.Manage",
                "Purchases.Invoices.Post",
                "Purchases.Invoices.Return",
                "Purchases.Invoices.View"
            });

        migrationBuilder.CreateIndex(
            name: "IX_InvoiceLines_CostCenterId",
            schema: "purchases",
            table: "InvoiceLines",
            column: "CostCenterId");

        migrationBuilder.CreateIndex(
            name: "IX_InvoiceLines_ItemId",
            schema: "purchases",
            table: "InvoiceLines",
            column: "ItemId");

        migrationBuilder.CreateIndex(
            name: "IX_InvoiceLines_UnitId",
            schema: "purchases",
            table: "InvoiceLines",
            column: "UnitId");

        migrationBuilder.CreateIndex(
            name: "IX_PurchaseInvoiceLines_InvoiceId",
            schema: "purchases",
            table: "InvoiceLines",
            column: "PurchaseInvoiceId");

        migrationBuilder.CreateIndex(
            name: "IX_PurchaseInvoiceLines_SourceLineId",
            schema: "purchases",
            table: "InvoiceLines",
            column: "SourceLineId",
            filter: "[SourceLineId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Invoices_BranchId",
            schema: "purchases",
            table: "Invoices",
            column: "BranchId");

        migrationBuilder.CreateIndex(
            name: "IX_Invoices_JournalEntryId",
            schema: "purchases",
            table: "Invoices",
            column: "JournalEntryId",
            unique: true,
            filter: "[JournalEntryId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Invoices_PatternRevisionId",
            schema: "purchases",
            table: "Invoices",
            column: "PatternRevisionId");

        migrationBuilder.CreateIndex(
            name: "IX_Invoices_SupplierId",
            schema: "purchases",
            table: "Invoices",
            column: "SupplierId");

        migrationBuilder.CreateIndex(
            name: "IX_Invoices_WarehouseId",
            schema: "purchases",
            table: "Invoices",
            column: "WarehouseId");

        migrationBuilder.CreateIndex(
            name: "IX_PurchaseInvoices_Company_Supplier",
            schema: "purchases",
            table: "Invoices",
            columns: new[] { "CompanyId", "SupplierId" });

        migrationBuilder.CreateIndex(
            name: "IX_PurchaseInvoices_ReturnOf",
            schema: "purchases",
            table: "Invoices",
            column: "ReturnOfInvoiceId",
            filter: "[ReturnOfInvoiceId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_PurchaseInvoices_Scope_Date",
            schema: "purchases",
            table: "Invoices",
            columns: new[] { "CompanyId", "BranchId", "InvoiceDateUtc" });

        migrationBuilder.CreateIndex(
            name: "UQ_PurchaseInvoices_Number",
            schema: "purchases",
            table: "Invoices",
            columns: new[] { "CompanyId", "BranchId", "InvoiceNumber" },
            unique: true,
            filter: "[InvoiceNumber] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "UQ_PurchaseInvoices_PostingKey",
            schema: "purchases",
            table: "Invoices",
            columns: new[] { "CompanyId", "BranchId", "PostingKey" },
            unique: true,
            filter: "[PostingKey] IS NOT NULL");

        // Seeded permissions are granted to the administrator role; other roles receive them explicitly.
        migrationBuilder.Sql("""
            INSERT INTO [security].[RolePermissions] ([RoleId], [PermissionCode], [CreatedAtUtc])
            SELECT r.[Id], p.[Code], SYSUTCDATETIME()
            FROM [security].[Roles] r CROSS JOIN [security].[Permissions] p
            WHERE r.[Code] = N'ADMINISTRATOR' AND p.[Code] LIKE N'Purchases.%'
            AND NOT EXISTS (SELECT 1 FROM [security].[RolePermissions] rp WHERE rp.[RoleId] = r.[Id] AND rp.[PermissionCode] = p.[Code]);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM [security].[RolePermissions] WHERE [PermissionCode] IN (N'Purchases.Invoices.Manage', N'Purchases.Invoices.Post', N'Purchases.Invoices.Return', N'Purchases.Invoices.View');
            """);
        migrationBuilder.DropTable(
            name: "InvoiceLines",
            schema: "purchases");

        migrationBuilder.DropTable(
            name: "Invoices",
            schema: "purchases");

        migrationBuilder.DeleteData(
            schema: "security",
            table: "Permissions",
            keyColumn: "Code",
            keyValue: "Purchases.Invoices.Manage");

        migrationBuilder.DeleteData(
            schema: "security",
            table: "Permissions",
            keyColumn: "Code",
            keyValue: "Purchases.Invoices.Post");

        migrationBuilder.DeleteData(
            schema: "security",
            table: "Permissions",
            keyColumn: "Code",
            keyValue: "Purchases.Invoices.Return");

        migrationBuilder.DeleteData(
            schema: "security",
            table: "Permissions",
            keyColumn: "Code",
            keyValue: "Purchases.Invoices.View");
    }
}
