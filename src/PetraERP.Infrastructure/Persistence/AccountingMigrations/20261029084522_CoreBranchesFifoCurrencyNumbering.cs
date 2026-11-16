using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace PetraERP.Infrastructure.Persistence.AccountingMigrations;
/// <inheritdoc />
public partial class CoreBranchesFifoCurrencyNumbering : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "DefaultCostCenterId",
            schema: "inventory",
            table: "Warehouses",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "InTransitAccountId",
            schema: "inventory",
            table: "Warehouses",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "InventoryAccountId",
            schema: "inventory",
            table: "Warehouses",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "ParentWarehouseId",
            schema: "inventory",
            table: "Warehouses",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.DropCheckConstraint(
            name: "CK_CurrencyAudit_Action",
            schema: "system",
            table: "CurrencyAudit");

        migrationBuilder.AddColumn<string>(
            name: "Details",
            schema: "system",
            table: "CurrencyAudit",
            type: "nvarchar(2000)",
            maxLength: 2000,
            nullable: true);

        migrationBuilder.AddColumn<byte>(
            name: "DecimalPlaces",
            schema: "system",
            table: "Currencies",
            type: "tinyint",
            nullable: false,
            defaultValue: (byte)2);

        migrationBuilder.AddColumn<string>(
            name: "MinorUnitNameArabic",
            schema: "system",
            table: "Currencies",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "MinorUnitNameEnglish",
            schema: "system",
            table: "Currencies",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<byte>(
            name: "RoundingMode",
            schema: "system",
            table: "Currencies",
            type: "tinyint",
            nullable: false,
            defaultValue: (byte)1);

        migrationBuilder.AddColumn<string>(
            name: "Symbol",
            schema: "system",
            table: "Currencies",
            type: "nvarchar(8)",
            maxLength: 8,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Address",
            schema: "system",
            table: "Branches",
            type: "nvarchar(400)",
            maxLength: 400,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "DefaultBankAccountId",
            schema: "system",
            table: "Branches",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "DefaultCashAccountId",
            schema: "system",
            table: "Branches",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "DefaultCostCenterId",
            schema: "system",
            table: "Branches",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "DefaultWarehouseId",
            schema: "system",
            table: "Branches",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Email",
            schema: "system",
            table: "Branches",
            type: "nvarchar(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Notes",
            schema: "system",
            table: "Branches",
            type: "nvarchar(1000)",
            maxLength: 1000,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "NumberPrefix",
            schema: "system",
            table: "Branches",
            type: "nvarchar(8)",
            maxLength: 8,
            nullable: true,
            collation: "Latin1_General_100_BIN2");

        migrationBuilder.AddColumn<DateOnly>(
            name: "OpenedOn",
            schema: "system",
            table: "Branches",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "ParentBranchId",
            schema: "system",
            table: "Branches",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Phone",
            schema: "system",
            table: "Branches",
            type: "nvarchar(50)",
            maxLength: 50,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "BranchAccountLinks",
            schema: "ledger",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Role = table.Column<byte>(type: "tinyint", nullable: false),
                AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BranchAccountLinks", x => x.Id);
                table.CheckConstraint("CK_BranchAccountLinks_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_BranchAccountLinks_Range", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
                table.CheckConstraint("CK_BranchAccountLinks_Role", "[Role] BETWEEN 1 AND 11");
                table.CheckConstraint("CK_BranchAccountLinks_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_BranchAccountLinks_Accounts_AccountId",
                    column: x => x.AccountId,
                    principalSchema: "ledger",
                    principalTable: "Accounts",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_BranchAccountLinks_ActorReferences_CompanyId_CreatedByUserId",
                    columns: x => new { x.CompanyId, x.CreatedByUserId },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_BranchAccountLinks_Branches_CompanyId_BranchId",
                    columns: x => new { x.CompanyId, x.BranchId },
                    principalSchema: "system",
                    principalTable: "Branches",
                    principalColumns: new[] { "CompanyId", "Id" });
            });

        migrationBuilder.CreateTable(
            name: "BranchAudit",
            schema: "ledger",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Action = table.Column<byte>(type: "tinyint", nullable: false),
                Detail = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                Reason = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BranchAudit", x => x.Id);
                table.CheckConstraint("CK_BranchAudit_Action", "[Action] BETWEEN 1 AND 5");
                table.CheckConstraint("CK_BranchAudit_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_BranchAudit_Reason", "DATALENGTH([Reason]) > 0 AND DATALENGTH([Reason]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Reason]))");
                table.CheckConstraint("CK_BranchAudit_Utc", "DATEPART(TZOFFSET, [OccurredAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_BranchAudit_ActorReferences_CompanyId_ActorUserId",
                    columns: x => new { x.CompanyId, x.ActorUserId },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_BranchAudit_Branches_CompanyId_BranchId",
                    columns: x => new { x.CompanyId, x.BranchId },
                    principalSchema: "system",
                    principalTable: "Branches",
                    principalColumns: new[] { "CompanyId", "Id" });
            });

        migrationBuilder.CreateTable(
            name: "DocumentNumberChanges",
            schema: "system",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Family = table.Column<byte>(type: "tinyint", nullable: false),
                DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OldNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                NewNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                State = table.Column<byte>(type: "tinyint", nullable: false),
                RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                DecidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                DecidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                DecisionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DocumentNumberChanges", x => x.Id);
                table.CheckConstraint("CK_DocumentNumberChanges_Decision", "([State] = 1 AND [DecidedByUserId] IS NULL AND [DecidedAtUtc] IS NULL) OR ([State] <> 1 AND [DecidedByUserId] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL)");
                table.CheckConstraint("CK_DocumentNumberChanges_Different", "[OldNumber] IS NULL OR [OldNumber] <> [NewNumber]");
                table.CheckConstraint("CK_DocumentNumberChanges_Family", "[Family] BETWEEN 1 AND 3");
                table.CheckConstraint("CK_DocumentNumberChanges_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_DocumentNumberChanges_NewNumber", "DATALENGTH([NewNumber]) > 0 AND DATALENGTH([NewNumber]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [NewNumber]))");
                table.CheckConstraint("CK_DocumentNumberChanges_Reason", "DATALENGTH([Reason]) > 0 AND DATALENGTH([Reason]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Reason]))");
                table.CheckConstraint("CK_DocumentNumberChanges_State", "[State] BETWEEN 1 AND 4");
                table.CheckConstraint("CK_DocumentNumberChanges_TwoPerson", "[State] <> 2 OR ([DecidedByUserId] IS NOT NULL AND [DecidedByUserId] <> [RequestedByUserId])");
                table.CheckConstraint("CK_DocumentNumberChanges_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_DocumentNumberChanges_ActorReferences_CompanyId_DecidedByUserId",
                    columns: x => new { x.CompanyId, x.DecidedByUserId },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_DocumentNumberChanges_ActorReferences_CompanyId_RequestedByUserId",
                    columns: x => new { x.CompanyId, x.RequestedByUserId },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_DocumentNumberChanges_Branches_CompanyId_BranchId",
                    columns: x => new { x.CompanyId, x.BranchId },
                    principalSchema: "system",
                    principalTable: "Branches",
                    principalColumns: new[] { "CompanyId", "Id" });
            });

        migrationBuilder.CreateTable(
            name: "InventoryCostLayers",
            schema: "inventory",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ReceiptMovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SourceSequence = table.Column<long>(type: "bigint", nullable: false),
                ReceivedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                QuantityReceived = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                QuantityRemaining = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                ValueReceived = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                ValueRemaining = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_InventoryCostLayers", x => x.Id);
                table.CheckConstraint("CK_InventoryCostLayers_Exhausted", "[QuantityRemaining] > 0 OR [ValueRemaining] = 0");
                table.CheckConstraint("CK_InventoryCostLayers_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_InventoryCostLayers_Received", "[QuantityReceived] > 0 AND [ValueReceived] >= 0");
                table.CheckConstraint("CK_InventoryCostLayers_Remaining", "[QuantityRemaining] >= 0 AND [QuantityRemaining] <= [QuantityReceived] AND [ValueRemaining] >= 0 AND [ValueRemaining] <= [ValueReceived]");
                table.CheckConstraint("CK_InventoryCostLayers_Utc", "DATEPART(TZOFFSET, [ReceivedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_InventoryCostLayers_StockMovements_ReceiptMovementId",
                    column: x => x.ReceiptMovementId,
                    principalSchema: "inventory",
                    principalTable: "StockMovements",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "DocumentNumberAudit",
            schema: "system",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Sequence = table.Column<long>(type: "bigint", nullable: false),
                RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Family = table.Column<byte>(type: "tinyint", nullable: false),
                DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OldNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                NewNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                PreviousHash = table.Column<string>(type: "char(64)", nullable: false, collation: "Latin1_General_100_BIN2"),
                Hash = table.Column<string>(type: "char(64)", nullable: false, collation: "Latin1_General_100_BIN2")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DocumentNumberAudit", x => x.Id);
                table.CheckConstraint("CK_DocumentNumberAudit_Family", "[Family] BETWEEN 1 AND 3");
                table.CheckConstraint("CK_DocumentNumberAudit_Hash", "LEN([Hash]) = 64 AND LEN([PreviousHash]) = 64");
                table.CheckConstraint("CK_DocumentNumberAudit_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_DocumentNumberAudit_Sequence", "[Sequence] > 0");
                table.CheckConstraint("CK_DocumentNumberAudit_TwoPerson", "[ApprovedByUserId] <> [RequestedByUserId]");
                table.CheckConstraint("CK_DocumentNumberAudit_Utc", "DATEPART(TZOFFSET, [OccurredAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_DocumentNumberAudit_ActorReferences_CompanyId_ApprovedByUserId",
                    columns: x => new { x.CompanyId, x.ApprovedByUserId },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_DocumentNumberAudit_ActorReferences_CompanyId_RequestedByUserId",
                    columns: x => new { x.CompanyId, x.RequestedByUserId },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_DocumentNumberAudit_DocumentNumberChanges_RequestId",
                    column: x => x.RequestId,
                    principalSchema: "system",
                    principalTable: "DocumentNumberChanges",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateTable(
            name: "InventoryCostAllocations",
            schema: "inventory",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                MovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Quantity = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                Cost = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                RecordedSequence = table.Column<long>(type: "bigint", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_InventoryCostAllocations", x => x.Id);
                table.CheckConstraint("CK_InventoryCostAllocations_Amounts", "[Quantity] <> 0 AND SIGN([Quantity]) = SIGN([Cost]) OR ([Quantity] <> 0 AND [Cost] = 0)");
                table.CheckConstraint("CK_InventoryCostAllocations_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_InventoryCostAllocations_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_InventoryCostAllocations_InventoryCostLayers_LayerId",
                    column: x => x.LayerId,
                    principalSchema: "inventory",
                    principalTable: "InventoryCostLayers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_InventoryCostAllocations_StockMovements_MovementId",
                    column: x => x.MovementId,
                    principalSchema: "inventory",
                    principalTable: "StockMovements",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.AddCheckConstraint(
            name: "CK_CurrencyAudit_Action",
            schema: "system",
            table: "CurrencyAudit",
            sql: "[Action] IN (1, 2, 3)");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Currencies_DecimalPlaces",
            schema: "system",
            table: "Currencies",
            sql: "[DecimalPlaces] BETWEEN 0 AND 4");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Currencies_RoundingMode",
            schema: "system",
            table: "Currencies",
            sql: "[RoundingMode] IN (1, 2, 3, 4)");

        migrationBuilder.CreateIndex(
            name: "IX_Branches_CompanyId_NumberPrefix",
            schema: "system",
            table: "Branches",
            columns: new[] { "CompanyId", "NumberPrefix" },
            unique: true,
            filter: "[NumberPrefix] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Branches_CompanyId_ParentBranchId",
            schema: "system",
            table: "Branches",
            columns: new[] { "CompanyId", "ParentBranchId" });

        migrationBuilder.AddCheckConstraint(
            name: "CK_Branches_NumberPrefix",
            schema: "system",
            table: "Branches",
            sql: "[NumberPrefix] IS NULL OR ([NumberPrefix] NOT LIKE '%[^A-Z0-9]%' AND DATALENGTH([NumberPrefix]) BETWEEN 1 AND 8)");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Branches_ParentNotSelf",
            schema: "system",
            table: "Branches",
            sql: "[ParentBranchId] IS NULL OR [ParentBranchId] <> [Id]");

        migrationBuilder.CreateIndex(
            name: "IX_BranchAccountLinks_AccountId",
            schema: "ledger",
            table: "BranchAccountLinks",
            column: "AccountId");

        migrationBuilder.CreateIndex(
            name: "IX_BranchAccountLinks_CompanyId_BranchId_Role_EffectiveFrom",
            schema: "ledger",
            table: "BranchAccountLinks",
            columns: new[] { "CompanyId", "BranchId", "Role", "EffectiveFrom" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_BranchAccountLinks_CompanyId_CreatedByUserId",
            schema: "ledger",
            table: "BranchAccountLinks",
            columns: new[] { "CompanyId", "CreatedByUserId" });

        migrationBuilder.CreateIndex(
            name: "IX_BranchAudit_CompanyId_ActorUserId",
            schema: "ledger",
            table: "BranchAudit",
            columns: new[] { "CompanyId", "ActorUserId" });

        migrationBuilder.CreateIndex(
            name: "IX_BranchAudit_CompanyId_BranchId_OccurredAtUtc",
            schema: "ledger",
            table: "BranchAudit",
            columns: new[] { "CompanyId", "BranchId", "OccurredAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_DocumentNumberAudit_CompanyId_ApprovedByUserId",
            schema: "system",
            table: "DocumentNumberAudit",
            columns: new[] { "CompanyId", "ApprovedByUserId" });

        migrationBuilder.CreateIndex(
            name: "IX_DocumentNumberAudit_CompanyId_BranchId_Sequence",
            schema: "system",
            table: "DocumentNumberAudit",
            columns: new[] { "CompanyId", "BranchId", "Sequence" });

        migrationBuilder.CreateIndex(
            name: "IX_DocumentNumberAudit_CompanyId_RequestedByUserId",
            schema: "system",
            table: "DocumentNumberAudit",
            columns: new[] { "CompanyId", "RequestedByUserId" });

        migrationBuilder.CreateIndex(
            name: "IX_DocumentNumberAudit_CompanyId_Sequence",
            schema: "system",
            table: "DocumentNumberAudit",
            columns: new[] { "CompanyId", "Sequence" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_DocumentNumberAudit_Family_DocumentId",
            schema: "system",
            table: "DocumentNumberAudit",
            columns: new[] { "Family", "DocumentId" });

        migrationBuilder.CreateIndex(
            name: "IX_DocumentNumberAudit_RequestId",
            schema: "system",
            table: "DocumentNumberAudit",
            column: "RequestId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_DocumentNumberChanges_CompanyId_DecidedByUserId",
            schema: "system",
            table: "DocumentNumberChanges",
            columns: new[] { "CompanyId", "DecidedByUserId" });

        migrationBuilder.CreateIndex(
            name: "IX_DocumentNumberChanges_CompanyId_RequestedByUserId",
            schema: "system",
            table: "DocumentNumberChanges",
            columns: new[] { "CompanyId", "RequestedByUserId" });

        migrationBuilder.CreateIndex(
            name: "IX_DocumentNumberChanges_Scope",
            schema: "system",
            table: "DocumentNumberChanges",
            columns: new[] { "CompanyId", "BranchId", "State", "RequestedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "UX_DocumentNumberChanges_OpenNumber",
            schema: "system",
            table: "DocumentNumberChanges",
            columns: new[] { "CompanyId", "BranchId", "Family", "NewNumber" },
            unique: true,
            filter: "[State] = 1");

        migrationBuilder.CreateIndex(
            name: "UX_DocumentNumberChanges_OpenPerDocument",
            schema: "system",
            table: "DocumentNumberChanges",
            columns: new[] { "CompanyId", "Family", "DocumentId" },
            unique: true,
            filter: "[State] = 1");

        migrationBuilder.CreateIndex(
            name: "IX_InventoryCostAllocations_LayerId",
            schema: "inventory",
            table: "InventoryCostAllocations",
            column: "LayerId");

        migrationBuilder.CreateIndex(
            name: "IX_InventoryCostAllocations_MovementId",
            schema: "inventory",
            table: "InventoryCostAllocations",
            column: "MovementId");

        migrationBuilder.CreateIndex(
            name: "IX_InventoryCostLayers_Open",
            schema: "inventory",
            table: "InventoryCostLayers",
            columns: new[] { "CompanyId", "WarehouseId", "ItemId", "SourceSequence" },
            filter: "[QuantityRemaining] > 0");

        migrationBuilder.CreateIndex(
            name: "IX_InventoryCostLayers_ReceiptMovementId",
            schema: "inventory",
            table: "InventoryCostLayers",
            column: "ReceiptMovementId",
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "FK_Branches_Branches_CompanyId_ParentBranchId",
            schema: "system",
            table: "Branches",
            columns: new[] { "CompanyId", "ParentBranchId" },
            principalSchema: "system",
            principalTable: "Branches",
            principalColumns: new[] { "CompanyId", "Id" });

        // Audit tables are append-only at the database as well (same protection as the other audit tables).
        migrationBuilder.Sql("""
            EXEC(N'CREATE TRIGGER [ledger].[TR_BranchAudit_Immutable] ON [ledger].[BranchAudit]
            AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON;
                THROW 51020, ''Accounting audit and posted movement history is immutable.'', 1;
            END;');
            """);
        migrationBuilder.Sql("""
            EXEC(N'CREATE TRIGGER [inventory].[TR_InventoryCostAllocations_Immutable] ON [inventory].[InventoryCostAllocations]
            AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON;
                THROW 51020, ''Accounting audit and posted movement history is immutable.'', 1;
            END;');
            """);
        migrationBuilder.Sql("""
            EXEC(N'CREATE TRIGGER [system].[TR_DocumentNumberAudit_Immutable] ON [system].[DocumentNumberAudit]
            AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON;
                THROW 51020, ''Accounting audit and posted movement history is immutable.'', 1;
            END;');
            """);

        // Existing currencies keep two decimals unless ISO 4217 says otherwise.
        migrationBuilder.Sql("UPDATE [system].[Currencies] SET [DecimalPlaces] = 3 WHERE [Code] IN ('KWD','BHD','OMR','JOD','IQD','LYD','TND');");
        migrationBuilder.Sql("UPDATE [system].[Currencies] SET [DecimalPlaces] = 0 WHERE [Code] IN ('DJF','KMF');");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "DefaultCostCenterId",
            schema: "inventory",
            table: "Warehouses");

        migrationBuilder.DropColumn(
            name: "InTransitAccountId",
            schema: "inventory",
            table: "Warehouses");

        migrationBuilder.DropColumn(
            name: "InventoryAccountId",
            schema: "inventory",
            table: "Warehouses");

        migrationBuilder.DropColumn(
            name: "ParentWarehouseId",
            schema: "inventory",
            table: "Warehouses");

        migrationBuilder.DropForeignKey(
            name: "FK_Branches_Branches_CompanyId_ParentBranchId",
            schema: "system",
            table: "Branches");

        migrationBuilder.DropTable(
            name: "BranchAccountLinks",
            schema: "ledger");

        migrationBuilder.DropTable(
            name: "BranchAudit",
            schema: "ledger");

        migrationBuilder.DropTable(
            name: "DocumentNumberAudit",
            schema: "system");

        migrationBuilder.DropTable(
            name: "InventoryCostAllocations",
            schema: "inventory");

        migrationBuilder.DropTable(
            name: "DocumentNumberChanges",
            schema: "system");

        migrationBuilder.DropTable(
            name: "InventoryCostLayers",
            schema: "inventory");

        migrationBuilder.DropCheckConstraint(
            name: "CK_CurrencyAudit_Action",
            schema: "system",
            table: "CurrencyAudit");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Currencies_DecimalPlaces",
            schema: "system",
            table: "Currencies");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Currencies_RoundingMode",
            schema: "system",
            table: "Currencies");

        migrationBuilder.DropIndex(
            name: "IX_Branches_CompanyId_NumberPrefix",
            schema: "system",
            table: "Branches");

        migrationBuilder.DropIndex(
            name: "IX_Branches_CompanyId_ParentBranchId",
            schema: "system",
            table: "Branches");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Branches_NumberPrefix",
            schema: "system",
            table: "Branches");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Branches_ParentNotSelf",
            schema: "system",
            table: "Branches");

        migrationBuilder.DropColumn(
            name: "Details",
            schema: "system",
            table: "CurrencyAudit");

        migrationBuilder.DropColumn(
            name: "DecimalPlaces",
            schema: "system",
            table: "Currencies");

        migrationBuilder.DropColumn(
            name: "MinorUnitNameArabic",
            schema: "system",
            table: "Currencies");

        migrationBuilder.DropColumn(
            name: "MinorUnitNameEnglish",
            schema: "system",
            table: "Currencies");

        migrationBuilder.DropColumn(
            name: "RoundingMode",
            schema: "system",
            table: "Currencies");

        migrationBuilder.DropColumn(
            name: "Symbol",
            schema: "system",
            table: "Currencies");

        migrationBuilder.DropColumn(
            name: "Address",
            schema: "system",
            table: "Branches");

        migrationBuilder.DropColumn(
            name: "DefaultBankAccountId",
            schema: "system",
            table: "Branches");

        migrationBuilder.DropColumn(
            name: "DefaultCashAccountId",
            schema: "system",
            table: "Branches");

        migrationBuilder.DropColumn(
            name: "DefaultCostCenterId",
            schema: "system",
            table: "Branches");

        migrationBuilder.DropColumn(
            name: "DefaultWarehouseId",
            schema: "system",
            table: "Branches");

        migrationBuilder.DropColumn(
            name: "Email",
            schema: "system",
            table: "Branches");

        migrationBuilder.DropColumn(
            name: "Notes",
            schema: "system",
            table: "Branches");

        migrationBuilder.DropColumn(
            name: "NumberPrefix",
            schema: "system",
            table: "Branches");

        migrationBuilder.DropColumn(
            name: "OpenedOn",
            schema: "system",
            table: "Branches");

        migrationBuilder.DropColumn(
            name: "ParentBranchId",
            schema: "system",
            table: "Branches");

        migrationBuilder.DropColumn(
            name: "Phone",
            schema: "system",
            table: "Branches");

        migrationBuilder.AddCheckConstraint(
            name: "CK_CurrencyAudit_Action",
            schema: "system",
            table: "CurrencyAudit",
            sql: "[Action] IN (1, 2)");
    }
}
