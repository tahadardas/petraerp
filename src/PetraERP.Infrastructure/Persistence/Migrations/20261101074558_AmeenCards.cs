using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace PetraERP.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AmeenCards : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Barcode",
            schema: "parties",
            table: "Parties",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "ConditionalDiscountAccountId",
            schema: "parties",
            table: "Parties",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "DiscountPercent",
            schema: "parties",
            table: "Parties",
            type: "decimal(9,4)",
            precision: 9,
            scale: 4,
            nullable: false,
            defaultValueSql: "0");

        migrationBuilder.AddColumn<bool>(
            name: "HideInSearch",
            schema: "parties",
            table: "Parties",
            type: "bit",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "Notes",
            schema: "parties",
            table: "Parties",
            type: "nvarchar(1000)",
            maxLength: 1000,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SalesPriceType",
            schema: "parties",
            table: "Parties",
            type: "nvarchar(32)",
            maxLength: 32,
            nullable: false,
            defaultValueSql: "N'CONSUMER'");

        migrationBuilder.AddColumn<string>(
            name: "Suffix",
            schema: "parties",
            table: "Parties",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Title",
            schema: "parties",
            table: "Parties",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "VatAccountId",
            schema: "parties",
            table: "Parties",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Color",
            schema: "inventory",
            table: "Items",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "ComponentPricing",
            schema: "inventory",
            table: "Items",
            type: "bit",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<int>(
            name: "DefaultUnitIndex",
            schema: "inventory",
            table: "Items",
            type: "int",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.AddColumn<bool>(
            name: "EnforceDatesOnIssue",
            schema: "inventory",
            table: "Items",
            type: "bit",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "EnforceDatesOnReceipt",
            schema: "inventory",
            table: "Items",
            type: "bit",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "EnforceSerialOnIssue",
            schema: "inventory",
            table: "Items",
            type: "bit",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "EnforceSerialOnReceipt",
            schema: "inventory",
            table: "Items",
            type: "bit",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<decimal>(
            name: "GiftPerQuantity",
            schema: "inventory",
            table: "Items",
            type: "decimal(19,4)",
            precision: 19,
            scale: 4,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "GiftQuantity",
            schema: "inventory",
            table: "Items",
            type: "decimal(19,4)",
            precision: 19,
            scale: 4,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "HideInSearch",
            schema: "inventory",
            table: "Items",
            type: "bit",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "ImagePath",
            schema: "inventory",
            table: "Items",
            type: "nvarchar(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "IntegerQuantityOnInvoice",
            schema: "inventory",
            table: "Items",
            type: "bit",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "IsAssembly",
            schema: "inventory",
            table: "Items",
            type: "bit",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<int>(
            name: "ItemKind",
            schema: "inventory",
            table: "Items",
            type: "int",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.AddColumn<decimal>(
            name: "LastPurchaseRate",
            schema: "inventory",
            table: "Items",
            type: "decimal(19,8)",
            precision: 19,
            scale: 8,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Location",
            schema: "inventory",
            table: "Items",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Manufacturer",
            schema: "inventory",
            table: "Items",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Measure",
            schema: "inventory",
            table: "Items",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Model",
            schema: "inventory",
            table: "Items",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "OriginCountry",
            schema: "inventory",
            table: "Items",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "PricingPolicy",
            schema: "inventory",
            table: "Items",
            type: "int",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.AddColumn<string>(
            name: "Quality",
            schema: "inventory",
            table: "Items",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Source",
            schema: "inventory",
            table: "Items",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Specifications",
            schema: "inventory",
            table: "Items",
            type: "nvarchar(2000)",
            maxLength: 2000,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "TracksExpiryDate",
            schema: "inventory",
            table: "Items",
            type: "bit",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "TracksProductionDate",
            schema: "inventory",
            table: "Items",
            type: "bit",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "TracksSerialNumber",
            schema: "inventory",
            table: "Items",
            type: "bit",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<decimal>(
            name: "Unit2Factor",
            schema: "inventory",
            table: "Items",
            type: "decimal(19,8)",
            precision: 19,
            scale: 8,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "Unit2FactorVariable",
            schema: "inventory",
            table: "Items",
            type: "bit",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<Guid>(
            name: "Unit2Id",
            schema: "inventory",
            table: "Items",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "Unit3Factor",
            schema: "inventory",
            table: "Items",
            type: "decimal(19,8)",
            precision: 19,
            scale: 8,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "Unit3FactorVariable",
            schema: "inventory",
            table: "Items",
            type: "bit",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<Guid>(
            name: "Unit3Id",
            schema: "inventory",
            table: "Items",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "AccountProfiles",
            schema: "ledger",
            columns: table => new
            {
                AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                Nature = table.Column<int>(type: "int", nullable: false),
                ClosingAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                DefaultCostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                BudgetKind = table.Column<int>(type: "int", nullable: false),
                BudgetAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                BudgetCurrencyCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                BudgetRate = table.Column<decimal>(type: "decimal(19,8)", precision: 19, scale: 8, nullable: true),
                IncludeCommercialPapers = table.Column<bool>(type: "bit", nullable: false),
                IncomeStatementCategory = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                EquityCategory = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                FinancialPositionCategory = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                CashFlowCategory = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AccountProfiles", x => x.AccountId);
                table.CheckConstraint("CK_AccountProfiles_Budget", "([BudgetKind] = 0 AND [BudgetAmount] IS NULL AND [BudgetCurrencyCode] IS NULL AND [BudgetRate] IS NULL) OR ([BudgetKind] IN (1, 2) AND [BudgetAmount] >= 0 AND [BudgetCurrencyCode] IS NOT NULL AND [BudgetRate] > 0)");
                table.CheckConstraint("CK_AccountProfiles_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_AccountProfiles_Nature", "[Nature] IN (1, 2, 3)");
                table.CheckConstraint("CK_AccountProfiles_NotSelfClosing", "[ClosingAccountId] IS NULL OR [ClosingAccountId] <> [AccountId]");
                table.CheckConstraint("CK_AccountProfiles_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_AccountProfiles_Accounts_AccountId",
                    column: x => x.AccountId,
                    principalSchema: "ledger",
                    principalTable: "Accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AccountProfiles_Accounts_ClosingAccountId",
                    column: x => x.ClosingAccountId,
                    principalSchema: "ledger",
                    principalTable: "Accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AccountProfiles_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Parties_ConditionalDiscountAccountId",
            schema: "parties",
            table: "Parties",
            column: "ConditionalDiscountAccountId");

        migrationBuilder.CreateIndex(
            name: "IX_Parties_VatAccountId",
            schema: "parties",
            table: "Parties",
            column: "VatAccountId");

        migrationBuilder.CreateIndex(
            name: "UQ_Parties_CompanyBarcode",
            schema: "parties",
            table: "Parties",
            columns: new[] { "CompanyId", "Barcode" },
            unique: true,
            filter: "[Barcode] IS NOT NULL");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Parties_ProfileDiscount",
            schema: "parties",
            table: "Parties",
            sql: "[DiscountPercent] >= 0 AND [DiscountPercent] <= 100");

        migrationBuilder.CreateIndex(
            name: "IX_Items_Unit2Id",
            schema: "inventory",
            table: "Items",
            column: "Unit2Id");

        migrationBuilder.CreateIndex(
            name: "IX_Items_Unit3Id",
            schema: "inventory",
            table: "Items",
            column: "Unit3Id");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Items_ProfileDefaultUnit",
            schema: "inventory",
            table: "Items",
            sql: "[DefaultUnitIndex] IN (1, 2, 3) AND ([DefaultUnitIndex] <> 2 OR [Unit2Id] IS NOT NULL) AND ([DefaultUnitIndex] <> 3 OR [Unit3Id] IS NOT NULL)");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Items_ProfileGift",
            schema: "inventory",
            table: "Items",
            sql: "([GiftQuantity] IS NULL OR [GiftQuantity] >= 0) AND ([GiftPerQuantity] IS NULL OR [GiftPerQuantity] >= 0)");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Items_ProfileKinds",
            schema: "inventory",
            table: "Items",
            sql: "[ItemKind] = 1 AND [PricingPolicy] = 1");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Items_ProfileLastPurchaseRate",
            schema: "inventory",
            table: "Items",
            sql: "[LastPurchaseRate] IS NULL OR [LastPurchaseRate] > 0");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Items_ProfileUnit3",
            schema: "inventory",
            table: "Items",
            sql: "([Unit3Id] IS NULL AND [Unit3Factor] IS NULL AND [Unit3FactorVariable] = 0) OR ([Unit3Id] IS NOT NULL AND [Unit2Id] IS NOT NULL AND [Unit3Id] <> [BaseUnitId] AND [Unit3Id] <> [Unit2Id] AND [Unit3Factor] > 0)");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Items_ProfileUnits",
            schema: "inventory",
            table: "Items",
            sql: "([Unit2Id] IS NULL AND [Unit2Factor] IS NULL AND [Unit2FactorVariable] = 0) OR ([Unit2Id] IS NOT NULL AND [Unit2Id] <> [BaseUnitId] AND [Unit2Factor] > 0)");

        migrationBuilder.CreateIndex(
            name: "IX_AccountProfiles_ClosingAccountId",
            schema: "ledger",
            table: "AccountProfiles",
            column: "ClosingAccountId");

        migrationBuilder.CreateIndex(
            name: "IX_AccountProfiles_Company",
            schema: "ledger",
            table: "AccountProfiles",
            column: "CompanyId");

        migrationBuilder.AddForeignKey(
            name: "FK_Items_UnitsOfMeasure_Unit2Id",
            schema: "inventory",
            table: "Items",
            column: "Unit2Id",
            principalSchema: "inventory",
            principalTable: "UnitsOfMeasure",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_Items_UnitsOfMeasure_Unit3Id",
            schema: "inventory",
            table: "Items",
            column: "Unit3Id",
            principalSchema: "inventory",
            principalTable: "UnitsOfMeasure",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_Parties_Accounts_ConditionalDiscountAccountId",
            schema: "parties",
            table: "Parties",
            column: "ConditionalDiscountAccountId",
            principalSchema: "ledger",
            principalTable: "Accounts",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_Parties_Accounts_VatAccountId",
            schema: "parties",
            table: "Parties",
            column: "VatAccountId",
            principalSchema: "ledger",
            principalTable: "Accounts",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Items_UnitsOfMeasure_Unit2Id",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropForeignKey(
            name: "FK_Items_UnitsOfMeasure_Unit3Id",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropForeignKey(
            name: "FK_Parties_Accounts_ConditionalDiscountAccountId",
            schema: "parties",
            table: "Parties");

        migrationBuilder.DropForeignKey(
            name: "FK_Parties_Accounts_VatAccountId",
            schema: "parties",
            table: "Parties");

        migrationBuilder.DropTable(
            name: "AccountProfiles",
            schema: "ledger");

        migrationBuilder.DropIndex(
            name: "IX_Parties_ConditionalDiscountAccountId",
            schema: "parties",
            table: "Parties");

        migrationBuilder.DropIndex(
            name: "IX_Parties_VatAccountId",
            schema: "parties",
            table: "Parties");

        migrationBuilder.DropIndex(
            name: "UQ_Parties_CompanyBarcode",
            schema: "parties",
            table: "Parties");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Parties_ProfileDiscount",
            schema: "parties",
            table: "Parties");

        migrationBuilder.DropIndex(
            name: "IX_Items_Unit2Id",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropIndex(
            name: "IX_Items_Unit3Id",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Items_ProfileDefaultUnit",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Items_ProfileGift",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Items_ProfileKinds",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Items_ProfileLastPurchaseRate",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Items_ProfileUnit3",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Items_ProfileUnits",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropColumn(
            name: "Barcode",
            schema: "parties",
            table: "Parties");

        migrationBuilder.DropColumn(
            name: "ConditionalDiscountAccountId",
            schema: "parties",
            table: "Parties");

        migrationBuilder.DropColumn(
            name: "DiscountPercent",
            schema: "parties",
            table: "Parties");

        migrationBuilder.DropColumn(
            name: "HideInSearch",
            schema: "parties",
            table: "Parties");

        migrationBuilder.DropColumn(
            name: "Notes",
            schema: "parties",
            table: "Parties");

        migrationBuilder.DropColumn(
            name: "SalesPriceType",
            schema: "parties",
            table: "Parties");

        migrationBuilder.DropColumn(
            name: "Suffix",
            schema: "parties",
            table: "Parties");

        migrationBuilder.DropColumn(
            name: "Title",
            schema: "parties",
            table: "Parties");

        migrationBuilder.DropColumn(
            name: "VatAccountId",
            schema: "parties",
            table: "Parties");

        migrationBuilder.DropColumn(
            name: "Color",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropColumn(
            name: "ComponentPricing",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropColumn(
            name: "DefaultUnitIndex",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropColumn(
            name: "EnforceDatesOnIssue",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropColumn(
            name: "EnforceDatesOnReceipt",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropColumn(
            name: "EnforceSerialOnIssue",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropColumn(
            name: "EnforceSerialOnReceipt",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropColumn(
            name: "GiftPerQuantity",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropColumn(
            name: "GiftQuantity",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropColumn(
            name: "HideInSearch",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropColumn(
            name: "ImagePath",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropColumn(
            name: "IntegerQuantityOnInvoice",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropColumn(
            name: "IsAssembly",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropColumn(
            name: "ItemKind",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropColumn(
            name: "LastPurchaseRate",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropColumn(
            name: "Location",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropColumn(
            name: "Manufacturer",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropColumn(
            name: "Measure",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropColumn(
            name: "Model",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropColumn(
            name: "OriginCountry",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropColumn(
            name: "PricingPolicy",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropColumn(
            name: "Quality",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropColumn(
            name: "Source",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropColumn(
            name: "Specifications",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropColumn(
            name: "TracksExpiryDate",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropColumn(
            name: "TracksProductionDate",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropColumn(
            name: "TracksSerialNumber",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropColumn(
            name: "Unit2Factor",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropColumn(
            name: "Unit2FactorVariable",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropColumn(
            name: "Unit2Id",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropColumn(
            name: "Unit3Factor",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropColumn(
            name: "Unit3FactorVariable",
            schema: "inventory",
            table: "Items");

        migrationBuilder.DropColumn(
            name: "Unit3Id",
            schema: "inventory",
            table: "Items");
    }
}
