#pragma warning disable CA1861
using System;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace PetraERP.Infrastructure.Persistence.Migrations;

/// <summary>Source only. Legacy audit balances remain NULL; no historical audit values are invented.</summary>
public partial class InventoryEngine : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateSequence<long>(name: "MovementOrder", schema: "inventory", startValue: 1L);
        migrationBuilder.CreateTable(name: "Documents", schema: "inventory", columns: table => new
        {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Kind = table.Column<int>(type: "int", nullable: false),
                State = table.Column<int>(type: "int", nullable: false),
                WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TargetWarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                DateUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false, precision: 7),
                Reason = table.Column<string>(type: "nvarchar(500)", nullable: false, maxLength: 500),
                CostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                InventoryAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                OffsetAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Number = table.Column<string>(type: "nvarchar(64)", nullable: true, maxLength: 64, collation: "Latin1_General_100_BIN2"),
                JournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ReversalJournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PostedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                PostedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true, precision: 7),
                PostKey = table.Column<string>(type: "nvarchar(128)", nullable: true, maxLength: 128, collation: "Latin1_General_100_BIN2"),
                PostVersion = table.Column<string>(type: "nvarchar(32)", nullable: true, maxLength: 32),
                PostReason = table.Column<string>(type: "nvarchar(500)", nullable: true, maxLength: 500),
                ReverseKey = table.Column<string>(type: "nvarchar(128)", nullable: true, maxLength: 128, collation: "Latin1_General_100_BIN2"),
                ReverseVersion = table.Column<string>(type: "nvarchar(32)", nullable: true, maxLength: 32),
                ReverseReason = table.Column<string>(type: "nvarchar(500)", nullable: true, maxLength: 500),
                ReversedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ReversedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true, precision: 7),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false, precision: 7),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false, precision: 7),
                RowVersion = table.Column<byte[]>(type: "rowversion", nullable: false, rowVersion: true),
                CreationFingerprint = table.Column<string>(type: "nvarchar(64)", nullable: true, maxLength: 64),
                CreationKey = table.Column<string>(type: "nvarchar(128)", nullable: true, maxLength: 128, collation: "Latin1_General_100_BIN2")
        }, constraints: table =>
        {
                table.PrimaryKey("PK_Documents", x => x.Id);
                table.ForeignKey("FK_Documents_Companies_CompanyId", x => x.CompanyId, "Companies", "Id", "system", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_Documents_Branches_BranchId", x => x.BranchId, "Branches", "Id", "system", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_Documents_Warehouses_WarehouseId", x => x.WarehouseId, "Warehouses", "Id", "inventory", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_Documents_Warehouses_TargetWarehouseId", x => x.TargetWarehouseId, "Warehouses", "Id", "inventory", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_Documents_Accounts_InventoryAccountId", x => x.InventoryAccountId, "Accounts", "Id", "ledger", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_Documents_Accounts_OffsetAccountId", x => x.OffsetAccountId, "Accounts", "Id", "ledger", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_Documents_CostCenters_CostCenterId", x => x.CostCenterId, "CostCenters", "Id", "ledger", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_Documents_JournalEntries_JournalId", x => x.JournalId, "JournalEntries", "Id", "ledger", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_Documents_JournalEntries_ReversalJournalId", x => x.ReversalJournalId, "JournalEntries", "Id", "ledger", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_Documents_Users_CreatedBy", x => x.CreatedBy, "Users", "Id", "security", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_Documents_Users_PostedBy", x => x.PostedBy, "Users", "Id", "security", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_Documents_Users_ReversedBy", x => x.ReversedBy, "Users", "Id", "security", onDelete: ReferentialAction.Restrict);
                table.CheckConstraint("CK_InventoryDocuments_Kind", "[Kind] IN (1,2,3)");
                table.CheckConstraint("CK_InventoryDocuments_State", "[State] IN (0,1,2)");
                table.CheckConstraint("CK_InventoryDocuments_Posted", "[State]=0 OR ([Number] IS NOT NULL AND [PostKey] IS NOT NULL AND [PostedBy] IS NOT NULL AND [PostedAtUtc] IS NOT NULL)");
                table.CheckConstraint("CK_InventoryDocuments_Reversed", "[State]<>2 OR ([ReverseKey] IS NOT NULL AND [ReversedBy] IS NOT NULL AND [ReversedAtUtc] IS NOT NULL)");
                table.CheckConstraint("CK_InventoryDocuments_Reason", "LEN(LTRIM(RTRIM([Reason])))>0");
                table.CheckConstraint("CK_InventoryDocuments_Utc", "DATEPART(TZOFFSET,[DateUtc])=0");
                table.CheckConstraint("CK_InventoryDocuments_Target", "([Kind]=1 AND [TargetWarehouseId] IS NOT NULL AND [TargetWarehouseId]<>[WarehouseId]) OR ([Kind]<>1 AND [TargetWarehouseId] IS NULL)");
                table.CheckConstraint("CK_InventoryDocuments_Mappings", "([Kind]=1 AND [InventoryAccountId] IS NULL AND [OffsetAccountId] IS NULL) OR ([Kind]<>1 AND [InventoryAccountId] IS NOT NULL AND [OffsetAccountId] IS NOT NULL AND [InventoryAccountId]<>[OffsetAccountId])");
                table.CheckConstraint("CK_InventoryDocuments_Center", "[Kind]<>3 OR [CostCenterId] IS NOT NULL");
        });
        migrationBuilder.CreateIndex(name: "IX_Documents_CompanyId_BranchId_PostKey", schema: "inventory", table: "Documents", columns: new[] { "CompanyId", "BranchId", "PostKey" }, unique: true, filter: "[PostKey] IS NOT NULL");
        migrationBuilder.CreateIndex(name: "IX_Documents_CompanyId_BranchId_ReverseKey", schema: "inventory", table: "Documents", columns: new[] { "CompanyId", "BranchId", "ReverseKey" }, unique: true, filter: "[ReverseKey] IS NOT NULL");
        migrationBuilder.CreateIndex(name: "IX_Documents_CompanyId_BranchId_Number", schema: "inventory", table: "Documents", columns: new[] { "CompanyId", "BranchId", "Number" }, unique: true, filter: "[Number] IS NOT NULL");
        migrationBuilder.CreateIndex(name: "IX_Documents_CompanyId_BranchId_DateUtc", schema: "inventory", table: "Documents", columns: new[] { "CompanyId", "BranchId", "DateUtc" }, unique: false);
        migrationBuilder.CreateIndex(name: "IX_Documents_CompanyId_BranchId_CreationKey", schema: "inventory", table: "Documents", columns: new[] { "CompanyId", "BranchId", "CreationKey" }, unique: true, filter: "[CreationKey] IS NOT NULL");
        migrationBuilder.CreateIndex(name: "IX_Documents_BranchId", schema: "inventory", table: "Documents", columns: new[] { "BranchId" }, unique: false);
        migrationBuilder.CreateIndex(name: "IX_Documents_WarehouseId", schema: "inventory", table: "Documents", columns: new[] { "WarehouseId" }, unique: false);
        migrationBuilder.CreateIndex(name: "IX_Documents_TargetWarehouseId", schema: "inventory", table: "Documents", columns: new[] { "TargetWarehouseId" }, unique: false);
        migrationBuilder.CreateIndex(name: "IX_Documents_InventoryAccountId", schema: "inventory", table: "Documents", columns: new[] { "InventoryAccountId" }, unique: false);
        migrationBuilder.CreateIndex(name: "IX_Documents_OffsetAccountId", schema: "inventory", table: "Documents", columns: new[] { "OffsetAccountId" }, unique: false);
        migrationBuilder.CreateIndex(name: "IX_Documents_CostCenterId", schema: "inventory", table: "Documents", columns: new[] { "CostCenterId" }, unique: false);
        migrationBuilder.CreateIndex(name: "IX_Documents_JournalId", schema: "inventory", table: "Documents", columns: new[] { "JournalId" }, unique: false);
        migrationBuilder.CreateIndex(name: "IX_Documents_ReversalJournalId", schema: "inventory", table: "Documents", columns: new[] { "ReversalJournalId" }, unique: false);
        migrationBuilder.CreateIndex(name: "IX_Documents_CreatedBy", schema: "inventory", table: "Documents", columns: new[] { "CreatedBy" }, unique: false);
        migrationBuilder.CreateIndex(name: "IX_Documents_PostedBy", schema: "inventory", table: "Documents", columns: new[] { "PostedBy" }, unique: false);
        migrationBuilder.CreateIndex(name: "IX_Documents_ReversedBy", schema: "inventory", table: "Documents", columns: new[] { "ReversedBy" }, unique: false);
        migrationBuilder.CreateTable(name: "DocumentLines", schema: "inventory", columns: table => new
        {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Position = table.Column<int>(type: "int", nullable: false),
                Quantity = table.Column<decimal>(type: "decimal(19,4)", nullable: false, precision: 19, scale: 4),
                BaseQuantity = table.Column<decimal>(type: "decimal(19,4)", nullable: false, precision: 19, scale: 4),
                SnapshotQuantity = table.Column<decimal>(type: "decimal(19,4)", nullable: false, precision: 19, scale: 4),
                SnapshotSequence = table.Column<long>(type: "bigint", nullable: false),
                Cost = table.Column<decimal>(type: "decimal(19,4)", nullable: false, precision: 19, scale: 4),
                OpeningUnitCost = table.Column<decimal>(type: "decimal(19,4)", nullable: true, precision: 19, scale: 4)
        }, constraints: table =>
        {
                table.PrimaryKey("PK_DocumentLines", x => x.Id);
                table.ForeignKey("FK_DocumentLines_Documents_DocumentId", x => x.DocumentId, "Documents", "Id", "inventory", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_DocumentLines_Items_ItemId", x => x.ItemId, "Items", "Id", "inventory", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_DocumentLines_UnitsOfMeasure_UnitId", x => x.UnitId, "UnitsOfMeasure", "Id", "inventory", onDelete: ReferentialAction.Restrict);
                table.CheckConstraint("CK_InventoryDocumentLines_Quantity", "[Quantity]>=0 AND [BaseQuantity]>=0 AND [Cost]>=0");
                table.CheckConstraint("CK_InventoryDocumentLines_OpeningCost", "[OpeningUnitCost] IS NULL OR [OpeningUnitCost]>=0");
        });
        migrationBuilder.CreateIndex(name: "IX_DocumentLines_DocumentId_ItemId", schema: "inventory", table: "DocumentLines", columns: new[] { "DocumentId", "ItemId" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_DocumentLines_ItemId", schema: "inventory", table: "DocumentLines", columns: new[] { "ItemId" }, unique: false);
        migrationBuilder.CreateIndex(name: "IX_DocumentLines_UnitId", schema: "inventory", table: "DocumentLines", columns: new[] { "UnitId" }, unique: false);
        migrationBuilder.CreateTable(name: "DocumentSequences", schema: "inventory", columns: table => new
        {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Year = table.Column<int>(type: "int", nullable: false),
                Kind = table.Column<int>(type: "int", nullable: false),
                NextValue = table.Column<long>(type: "bigint", nullable: false)
        }, constraints: table =>
        {
                table.PrimaryKey("PK_DocumentSequences", x => new { x.CompanyId, x.BranchId, x.Year, x.Kind });
                table.ForeignKey("FK_DocumentSequences_Companies_CompanyId", x => x.CompanyId, "Companies", "Id", "system", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_DocumentSequences_Branches_BranchId", x => x.BranchId, "Branches", "Id", "system", onDelete: ReferentialAction.Restrict);
                table.CheckConstraint("CK_InventorySequence_Value", "[NextValue]>=0");
        });
        migrationBuilder.CreateIndex(name: "IX_DocumentSequences_BranchId", schema: "inventory", table: "DocumentSequences", columns: new[] { "BranchId" }, unique: false);
        migrationBuilder.AddColumn<long>(name: "RecordedSequence", schema: "inventory", table: "StockMovements", type: "bigint", nullable: false, defaultValueSql: "NEXT VALUE FOR [inventory].[MovementOrder]");
        migrationBuilder.AlterColumn<long>(name: "RecordedSequence", schema: "inventory", table: "StockMovements", type: "bigint", nullable: false, oldClrType: typeof(long), oldType: "bigint", oldDefaultValueSql: "NEXT VALUE FOR [inventory].[MovementOrder]");
        migrationBuilder.AddColumn<DateTimeOffset>(name: "RecordedAtUtc", schema: "inventory", table: "StockMovements", type: "datetimeoffset(7)", nullable: true, precision: 7);
        migrationBuilder.AddColumn<decimal>(name: "QuantityBefore", schema: "inventory", table: "StockMovements", type: "decimal(19,4)", nullable: true, precision: 19, scale: 4);
        migrationBuilder.AddColumn<decimal>(name: "QuantityAfter", schema: "inventory", table: "StockMovements", type: "decimal(19,4)", nullable: true, precision: 19, scale: 4);
        migrationBuilder.AddColumn<decimal>(name: "ValueBefore", schema: "inventory", table: "StockMovements", type: "decimal(19,4)", nullable: true, precision: 19, scale: 4);
        migrationBuilder.AddColumn<decimal>(name: "ValueAfter", schema: "inventory", table: "StockMovements", type: "decimal(19,4)", nullable: true, precision: 19, scale: 4);
        migrationBuilder.AddColumn<Guid>(name: "ReversesMovementId", schema: "inventory", table: "StockMovements", type: "uniqueidentifier", nullable: true);
        migrationBuilder.CreateIndex(name: "IX_StockMovements_RecordedSequence", schema: "inventory", table: "StockMovements", column: "RecordedSequence", unique: true);
        migrationBuilder.CreateIndex(name: "IX_StockMovements_ReversesMovementId", schema: "inventory", table: "StockMovements", column: "ReversesMovementId", unique: true, filter: "[ReversesMovementId] IS NOT NULL");
        migrationBuilder.AddForeignKey(name: "FK_StockMovements_StockMovements_ReversesMovementId", schema: "inventory", table: "StockMovements", column: "ReversesMovementId", principalSchema: "inventory", principalTable: "StockMovements", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddColumn<Guid>(name: "VarianceAccountId", schema: "purchases", table: "Invoices", type: "uniqueidentifier", nullable: true);
        migrationBuilder.CreateIndex(name: "IX_Invoices_VarianceAccountId", schema: "purchases", table: "Invoices", column: "VarianceAccountId");
        migrationBuilder.AddForeignKey(name: "FK_Invoices_Accounts_VarianceAccountId", schema: "purchases", table: "Invoices", column: "VarianceAccountId", principalSchema: "ledger", principalTable: "Accounts", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddCheckConstraint(name: "CK_StockMovements_AuditBalance", schema: "inventory", table: "StockMovements", sql: "([QuantityBefore] IS NULL AND [QuantityAfter] IS NULL AND [ValueBefore] IS NULL AND [ValueAfter] IS NULL) OR ([QuantityBefore] IS NOT NULL AND [QuantityAfter] IS NOT NULL AND [ValueBefore] IS NOT NULL AND [ValueAfter] IS NOT NULL AND [QuantityBefore]>=0 AND [QuantityAfter]>=0 AND [ValueBefore]>=0 AND [ValueAfter]>=0 AND [QuantityAfter]=[QuantityBefore]+[BaseQuantity] AND [ValueAfter]=[ValueBefore]+CASE WHEN [BaseQuantity]<0 THEN -[TotalCost] ELSE [TotalCost] END AND [RecordedAtUtc] IS NOT NULL)");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(name: "CK_StockMovements_AuditBalance", schema: "inventory", table: "StockMovements");
        migrationBuilder.DropForeignKey(name: "FK_Invoices_Accounts_VarianceAccountId", schema: "purchases", table: "Invoices");
        migrationBuilder.DropIndex(name: "IX_Invoices_VarianceAccountId", schema: "purchases", table: "Invoices");
        migrationBuilder.DropColumn(name: "VarianceAccountId", schema: "purchases", table: "Invoices");
        migrationBuilder.DropTable(name: "DocumentLines", schema: "inventory");
        migrationBuilder.DropTable(name: "Documents", schema: "inventory");
        migrationBuilder.DropTable(name: "DocumentSequences", schema: "inventory");
        migrationBuilder.DropForeignKey(name: "FK_StockMovements_StockMovements_ReversesMovementId", schema: "inventory", table: "StockMovements");
        migrationBuilder.DropIndex(name: "IX_StockMovements_ReversesMovementId", schema: "inventory", table: "StockMovements");
        migrationBuilder.DropIndex(name: "IX_StockMovements_RecordedSequence", schema: "inventory", table: "StockMovements");
        migrationBuilder.DropColumn(name: "RecordedSequence", schema: "inventory", table: "StockMovements");
        migrationBuilder.DropColumn(name: "RecordedAtUtc", schema: "inventory", table: "StockMovements");
        migrationBuilder.DropColumn(name: "QuantityBefore", schema: "inventory", table: "StockMovements");
        migrationBuilder.DropColumn(name: "QuantityAfter", schema: "inventory", table: "StockMovements");
        migrationBuilder.DropColumn(name: "ValueBefore", schema: "inventory", table: "StockMovements");
        migrationBuilder.DropColumn(name: "ValueAfter", schema: "inventory", table: "StockMovements");
        migrationBuilder.DropColumn(name: "ReversesMovementId", schema: "inventory", table: "StockMovements");
        migrationBuilder.DropSequence(name: "MovementOrder", schema: "inventory");
    }
}
