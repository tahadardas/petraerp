#pragma warning disable CA1861
using System;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace PetraERP.Infrastructure.Persistence.Migrations;

/// <summary>Source only; preserve legacy prices and unclassified units. Review before applying to any data.</summary>
public partial class CurrencyUnitsPricing : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "ExchangeRates", schema: "system", columns: table => new
        {
            Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            CurrencyCode = table.Column<string>(type: "char(3)", nullable: false, collation: "Latin1_General_100_BIN2"),
            BaseCurrencyCode = table.Column<string>(type: "char(3)", nullable: false, collation: "Latin1_General_100_BIN2"),
            Rate = table.Column<decimal>(type: "decimal(19,8)", nullable: false, precision: 19, scale: 8),
            EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
            Source = table.Column<string>(type: "nvarchar(64)", nullable: false, maxLength: 64),
            Reason = table.Column<string>(type: "nvarchar(500)", nullable: false, maxLength: 500),
            CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false, precision: 7, defaultValueSql: "SYSUTCDATETIME()"),
            UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false, precision: 7, defaultValueSql: "SYSUTCDATETIME()"),
            RowVersion = table.Column<byte[]>(type: "rowversion", nullable: false, rowVersion: true)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_ExchangeRates", x => x.Id);
            table.ForeignKey("FK_ExchangeRates_Companies_CompanyId", x => x.CompanyId, "Companies", "Id", "system", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_ExchangeRates_Currencies_CurrencyCode", x => x.CurrencyCode, "Currencies", "Code", "system", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_ExchangeRates_Currencies_BaseCurrencyCode", x => x.BaseCurrencyCode, "Currencies", "Code", "system", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_ExchangeRates_Users_CreatedByUserId", x => x.CreatedByUserId, "Users", "Id", "security", onDelete: ReferentialAction.Restrict);
            table.CheckConstraint("CK_ExchangeRates_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
            table.CheckConstraint("CK_ExchangeRates_Rate", "[Rate] > 0");
            table.CheckConstraint("CK_ExchangeRates_Currencies", "[CurrencyCode] <> [BaseCurrencyCode]");
            table.CheckConstraint("CK_ExchangeRates_Source", "DATALENGTH([Source]) > 0 AND DATALENGTH([Source]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Source]))");
            table.CheckConstraint("CK_ExchangeRates_Reason", "DATALENGTH([Reason]) > 0 AND DATALENGTH([Reason]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Reason]))");
            table.CheckConstraint("CK_ExchangeRates_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
        });
        migrationBuilder.CreateIndex(name: "IX_ExchangeRates_CompanyCurrencyEffective", schema: "system", table: "ExchangeRates", columns: new[] { "CompanyId", "CurrencyCode", "EffectiveDate", "CreatedAtUtc" });
        migrationBuilder.CreateIndex(name: "IX_ExchangeRates_CurrencyCode", schema: "system", table: "ExchangeRates", columns: new[] { "CurrencyCode" });
        migrationBuilder.CreateIndex(name: "IX_ExchangeRates_BaseCurrencyCode", schema: "system", table: "ExchangeRates", columns: new[] { "BaseCurrencyCode" });
        migrationBuilder.CreateIndex(name: "IX_ExchangeRates_CreatedByUserId", schema: "system", table: "ExchangeRates", columns: new[] { "CreatedByUserId" });
        migrationBuilder.Sql("""
            EXEC(N'CREATE TRIGGER [system].[TR_ExchangeRates_AppendOnly] ON [system].[ExchangeRates] AFTER UPDATE, DELETE AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM deleted) THROW 51020, ''Currency history is append-only; append a correction.'', 1;
            END;');
            """);
        migrationBuilder.CreateTable(name: "CurrencyAudit", schema: "system", columns: table => new
        {
            Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            Action = table.Column<byte>(type: "tinyint", nullable: false),
            CurrencyCode = table.Column<string>(type: "char(3)", nullable: false, collation: "Latin1_General_100_BIN2"),
            PreviousCurrencyCode = table.Column<string>(type: "char(3)", nullable: true, collation: "Latin1_General_100_BIN2"),
            Reason = table.Column<string>(type: "nvarchar(500)", nullable: false, maxLength: 500),
            ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false, precision: 7)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_CurrencyAudit", x => x.Id);
            table.ForeignKey("FK_CurrencyAudit_Companies_CompanyId", x => x.CompanyId, "Companies", "Id", "system", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_CurrencyAudit_Users_ActorUserId", x => x.ActorUserId, "Users", "Id", "security", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_CurrencyAudit_Currencies_CurrencyCode", x => x.CurrencyCode, "Currencies", "Code", "system", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_CurrencyAudit_Currencies_PreviousCurrencyCode", x => x.PreviousCurrencyCode, "Currencies", "Code", "system", onDelete: ReferentialAction.Restrict);
            table.CheckConstraint("CK_CurrencyAudit_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
            table.CheckConstraint("CK_CurrencyAudit_Action", "[Action] IN (1, 2)");
            table.CheckConstraint("CK_CurrencyAudit_Reason", "DATALENGTH([Reason]) > 0 AND DATALENGTH([Reason]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Reason]))");
            table.CheckConstraint("CK_CurrencyAudit_Utc", "DATEPART(TZOFFSET, [OccurredAtUtc]) = 0");
        });
        migrationBuilder.CreateIndex(name: "IX_CurrencyAudit_CompanyTime", schema: "system", table: "CurrencyAudit", columns: new[] { "CompanyId", "OccurredAtUtc" });
        migrationBuilder.CreateIndex(name: "IX_CurrencyAudit_ActorUserId", schema: "system", table: "CurrencyAudit", columns: new[] { "ActorUserId" });
        migrationBuilder.CreateIndex(name: "IX_CurrencyAudit_CurrencyCode", schema: "system", table: "CurrencyAudit", columns: new[] { "CurrencyCode" });
        migrationBuilder.CreateIndex(name: "IX_CurrencyAudit_PreviousCurrencyCode", schema: "system", table: "CurrencyAudit", columns: new[] { "PreviousCurrencyCode" });
        migrationBuilder.Sql("""
            EXEC(N'CREATE TRIGGER [system].[TR_CurrencyAudit_AppendOnly] ON [system].[CurrencyAudit] AFTER UPDATE, DELETE AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM deleted) THROW 51020, ''Currency history is append-only; append a correction.'', 1;
            END;');
            """);
        migrationBuilder.AddColumn<byte>(name: "Dimension", schema: "inventory", table: "UnitsOfMeasure", type: "tinyint", nullable: false, defaultValue: (byte)0);
        migrationBuilder.AddCheckConstraint(name: "CK_UnitsOfMeasure_Dimension", schema: "inventory", table: "UnitsOfMeasure", sql: "[Dimension] BETWEEN 0 AND 7");
        migrationBuilder.DropIndex(name: "UQ_ItemPrices_CompanyItemUnitPriceListMinQty", schema: "inventory", table: "ItemPrices");
        migrationBuilder.AddColumn<Guid>(name: "PartyId", schema: "inventory", table: "ItemPrices", type: "uniqueidentifier", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "PatternId", schema: "inventory", table: "ItemPrices", type: "uniqueidentifier", nullable: true);
        migrationBuilder.AddColumn<DateOnly>(name: "ValidFrom", schema: "inventory", table: "ItemPrices", type: "date", nullable: true);
        migrationBuilder.AddColumn<DateOnly>(name: "ValidTo", schema: "inventory", table: "ItemPrices", type: "date", nullable: true);
        migrationBuilder.CreateIndex(name: "IX_ItemPrices_PartyId", schema: "inventory", table: "ItemPrices", column: "PartyId");
        migrationBuilder.AddForeignKey(name: "FK_ItemPrices_Parties_PartyId", schema: "inventory", table: "ItemPrices", column: "PartyId", principalSchema: "parties", principalTable: "Parties", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.CreateIndex(name: "IX_ItemPrices_PatternId", schema: "inventory", table: "ItemPrices", column: "PatternId");
        migrationBuilder.AddForeignKey(name: "FK_ItemPrices_DocumentPatterns_PatternId", schema: "inventory", table: "ItemPrices", column: "PatternId", principalSchema: "patterns", principalTable: "DocumentPatterns", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.CreateIndex(name: "UQ_ItemPrices_Scope", schema: "inventory", table: "ItemPrices", columns: new[] { "CompanyId", "ItemId", "UnitId", "PriceListCode", "CurrencyCode", "PartyId", "PatternId", "MinQuantity", "ValidFrom" }, unique: true);
        migrationBuilder.AddCheckConstraint(name: "CK_ItemPrices_Validity", schema: "inventory", table: "ItemPrices", sql: "[ValidFrom] IS NULL OR [ValidTo] IS NULL OR [ValidTo] >= [ValidFrom]");
        migrationBuilder.Sql("""
            EXEC(N'CREATE OR ALTER TRIGGER [system].[TR_Companies_BaseCurrency] ON [system].[Companies] AFTER UPDATE AS
            BEGIN
             SET NOCOUNT ON;
             IF EXISTS (SELECT 1 FROM inserted i JOIN [system].[CompanyAdditionalCurrencies] a WITH(UPDLOCK,HOLDLOCK) ON a.CompanyId=i.Id AND a.CurrencyCode=i.BaseCurrencyCode)
              THROW 51001, ''Base currency cannot be an additional company currency.'', 1;
             IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON i.Id=d.Id WHERE i.BaseCurrencyCode<>d.BaseCurrencyCode AND (
              EXISTS(SELECT 1 FROM [ledger].[JournalEntries] x WHERE x.CompanyId=i.Id) OR
              EXISTS(SELECT 1 FROM [sales].[Invoices] x WHERE x.CompanyId=i.Id) OR
              EXISTS(SELECT 1 FROM [purchases].[Invoices] x WHERE x.CompanyId=i.Id) OR
              EXISTS(SELECT 1 FROM [treasury].[Vouchers] x WHERE x.CompanyId=i.Id) OR
              EXISTS(SELECT 1 FROM [inventory].[Documents] x WHERE x.CompanyId=i.Id) OR
              EXISTS(SELECT 1 FROM [inventory].[StockMovements] x WHERE x.CompanyId=i.Id) OR
              EXISTS(SELECT 1 FROM [inventory].[ItemPrices] x WHERE x.CompanyId=i.Id) OR
              EXISTS(SELECT 1 FROM [system].[ExchangeRates] x WHERE x.CompanyId=i.Id)))
              THROW 51021, ''Base currency is locked after financial or inventory data exists.'', 1;
            END;');
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            EXEC(N'CREATE OR ALTER TRIGGER [system].[TR_Companies_BaseCurrency] ON [system].[Companies] AFTER UPDATE AS
            BEGIN
             SET NOCOUNT ON;
             IF EXISTS (SELECT 1 FROM inserted i JOIN [system].[CompanyAdditionalCurrencies] a WITH(UPDLOCK,HOLDLOCK) ON a.CompanyId=i.Id AND a.CurrencyCode=i.BaseCurrencyCode)
              THROW 51001, ''Base currency cannot be an additional company currency.'', 1;
            END;');
            """);
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS [system].[TR_ExchangeRates_AppendOnly];");
        migrationBuilder.DropTable(name: "ExchangeRates", schema: "system");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS [system].[TR_CurrencyAudit_AppendOnly];");
        migrationBuilder.DropTable(name: "CurrencyAudit", schema: "system");
        migrationBuilder.DropCheckConstraint(name: "CK_UnitsOfMeasure_Dimension", schema: "inventory", table: "UnitsOfMeasure");
        migrationBuilder.DropColumn(name: "Dimension", schema: "inventory", table: "UnitsOfMeasure");
        migrationBuilder.DropCheckConstraint(name: "CK_ItemPrices_Validity", schema: "inventory", table: "ItemPrices");
        migrationBuilder.DropIndex(name: "UQ_ItemPrices_Scope", schema: "inventory", table: "ItemPrices");
        migrationBuilder.DropForeignKey(name: "FK_ItemPrices_Parties_PartyId", schema: "inventory", table: "ItemPrices");
        migrationBuilder.DropIndex(name: "IX_ItemPrices_PartyId", schema: "inventory", table: "ItemPrices");
        migrationBuilder.DropForeignKey(name: "FK_ItemPrices_DocumentPatterns_PatternId", schema: "inventory", table: "ItemPrices");
        migrationBuilder.DropIndex(name: "IX_ItemPrices_PatternId", schema: "inventory", table: "ItemPrices");
        migrationBuilder.DropColumn(name: "PartyId", schema: "inventory", table: "ItemPrices");
        migrationBuilder.DropColumn(name: "PatternId", schema: "inventory", table: "ItemPrices");
        migrationBuilder.DropColumn(name: "ValidFrom", schema: "inventory", table: "ItemPrices");
        migrationBuilder.DropColumn(name: "ValidTo", schema: "inventory", table: "ItemPrices");
        migrationBuilder.CreateIndex(name: "UQ_ItemPrices_CompanyItemUnitPriceListMinQty", schema: "inventory", table: "ItemPrices", columns: new[] { "CompanyId", "ItemId", "UnitId", "PriceListCode", "MinQuantity" }, unique: true);
    }
}
