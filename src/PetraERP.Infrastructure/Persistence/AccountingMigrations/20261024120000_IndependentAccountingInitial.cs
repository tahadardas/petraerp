using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetraERP.Infrastructure.Persistence.AccountingMigrations;
/// <inheritdoc />
public partial class IndependentAccountingInitial : Migration
{
    private static readonly string[] KeyColumns001 = ["CompanyId", "Id"];
    private static readonly string[] KeyColumns002 = ["CompanyId", "FiscalYearId", "Id"];
    private static readonly string[] KeyColumns003 = ["CompanyId", "AccountId", "OccurredAtUtc", "EventId"];
    private static readonly string[] KeyColumns004 = ["CompanyId", "AccountId"];
    private static readonly string[] KeyColumns005 = ["CompanyId", "ActorUserId"];
    private static readonly string[] KeyColumns006 = ["CompanyId", "WorkingBranchId"];
    private static readonly string[] KeyColumns007 = ["CompanyId", "OccurredAtUtc"];
    private static readonly string[] KeyColumns008 = ["CompanyId", "RangeStart", "RangeEnd"];
    private static readonly string[] KeyColumns009 = ["FiscalYearId", "Code"];
    private static readonly string[] KeyColumns010 = ["CompanyId", "IsActive", "PostingAllowed"];
    private static readonly string[] KeyColumns011 = ["CompanyId", "Code"];
    private static readonly string[] KeyColumns012 = ["VoucherId", "PurchaseInvoiceId"];
    private static readonly string[] KeyColumns013 = ["VoucherId", "SalesInvoiceId"];
    private static readonly string[] KeyColumns014 = ["CompanyId", "BranchId", "PaperNumber"];
    private static readonly string[] KeyColumns015 = ["CompanyId", "BranchId", "Status"];
    private static readonly string[] KeyColumns016 = ["CompanyId", "CreatedByUserId"];
    private static readonly string[] KeyColumns017 = ["CompanyId", "DueDate", "Status"];
    private static readonly string[] KeyColumns018 = ["CompanyId", "CostCenterId", "OccurredAtUtc", "EventId"];
    private static readonly string[] KeyColumns019 = ["CompanyId", "BranchId", "InvoiceId"];
    private static readonly string[] KeyColumns020 = ["DocumentId", "ItemId"];
    private static readonly string[] KeyColumns021 = ["CompanyId", "IsActive"];
    private static readonly string[] KeyColumns022 = ["CompanyId", "OperationKind", "Code"];
    private static readonly string[] KeyColumns023 = ["CompanyId", "BranchId", "CreationKey"];
    private static readonly string[] KeyColumns024 = ["CompanyId", "BranchId", "DateUtc"];
    private static readonly string[] KeyColumns025 = ["CompanyId", "BranchId", "Number"];
    private static readonly string[] KeyColumns026 = ["CompanyId", "BranchId", "PostKey"];
    private static readonly string[] KeyColumns027 = ["CompanyId", "BranchId", "ReverseKey"];
    private static readonly string[] KeyColumns028 = ["CompanyId", "CreatedBy"];
    private static readonly string[] KeyColumns029 = ["CompanyId", "PostedBy"];
    private static readonly string[] KeyColumns030 = ["CompanyId", "ReversedBy"];
    private static readonly string[] KeyColumns031 = ["CompanyId", "CurrencyCode", "EffectiveDate", "CreatedAtUtc"];
    private static readonly string[] KeyColumns032 = ["CompanyId", "RangeStart"];
    private static readonly string[] KeyColumns033 = ["CompanyId", "PostedByUserId"];
    private static readonly string[] KeyColumns034 = ["CompanyId", "SupplierId"];
    private static readonly string[] KeyColumns035 = ["CompanyId", "BranchId", "InvoiceDateUtc"];
    private static readonly string[] KeyColumns036 = ["CompanyId", "BranchId", "InvoiceNumber"];
    private static readonly string[] KeyColumns037 = ["CompanyId", "BranchId", "PostingKey"];
    private static readonly string[] KeyColumns038 = ["CompanyId", "ApprovedByUserId"];
    private static readonly string[] KeyColumns039 = ["CompanyId", "CustomerId"];
    private static readonly string[] KeyColumns040 = ["CompanyId", "InvoiceDateUtc"];
    private static readonly string[] KeyColumns041 = ["CompanyId", "Status"];
    private static readonly string[] KeyColumns042 = ["CompanyId", "ItemId"];
    private static readonly string[] KeyColumns043 = ["CompanyId", "Barcode"];
    private static readonly string[] KeyColumns044 = ["CompanyId", "ParentId"];
    private static readonly string[] KeyColumns045 = ["CompanyId", "ItemId", "UnitId", "PriceListCode", "CurrencyCode", "PartyId", "PatternId", "MinQuantity", "ValidFrom"];
    private static readonly string[] KeyColumns046 = ["CompanyId", "GroupId", "IsActive"];
    private static readonly string[] KeyColumns047 = ["CompanyId", "NormalizedSearchText"];
    private static readonly string[] KeyColumns048 = ["CompanyId", "JournalId", "Revision"];
    private static readonly string[] KeyColumns049 = ["JournalId", "Revision"];
    private static readonly string[] KeyColumns050 = ["CompanyId", "BranchId", "State", "AccountingDate"];
    private static readonly string[] KeyColumns051 = ["CompanyId", "PostingFiscalYearId", "PostingPeriodId"];
    private static readonly string[] KeyColumns052 = ["CompanyId", "BranchId", "PostingFiscalYearId", "PostingSequence"];
    private static readonly string[] KeyColumns053 = ["CompanyId", "AccountId", "JournalId"];
    private static readonly string[] KeyColumns054 = ["CompanyId", "CostCenterId"];
    private static readonly string[] KeyColumns055 = ["CompanyId", "Role", "IsActive"];
    private static readonly string[] KeyColumns056 = ["CompanyId", "IsCashCustomer"];
    private static readonly string[] KeyColumns057 = ["PatternId", "BranchId"];
    private static readonly string[] KeyColumns058 = ["PatternId", "RevisionNumber"];
    private static readonly string[] KeyColumns059 = ["CompanyId", "FiscalYearId", "PeriodId"];
    private static readonly string[] KeyColumns060 = ["CompanyId", "PeriodId", "OccurredAtUtc"];
    private static readonly string[] KeyColumns061 = ["CompanyId", "ActorId"];
    private static readonly string[] KeyColumns062 = ["CompanyId", "PartyId", "DateUtc"];
    private static readonly string[] KeyColumns063 = ["CompanyId", "BranchId", "JournalId"];
    private static readonly string[] KeyColumns064 = ["CompanyId", "FiscalYearId"];
    private static readonly string[] KeyColumns065 = ["BatchId", "PurchaseInvoiceId"];
    private static readonly string[] KeyColumns066 = ["BatchId", "SalesInvoiceId"];
    private static readonly string[] KeyColumns067 = ["CompanyId", "BranchId", "RequestKey"];
    private static readonly string[] KeyColumns068 = ["VoucherId", "DateUtc"];
    private static readonly string[] KeyColumns069 = ["CompanyId", "BranchId"];
    private static readonly string[] KeyColumns070 = ["CompanyId", "AdjustmentNumber"];
    private static readonly string[] KeyColumns071 = ["CompanyId", "DocumentType", "DocumentId"];
    private static readonly string[] KeyColumns072 = ["CompanyId", "ItemId", "MovementDateUtc"];
    private static readonly string[] KeyColumns073 = ["CompanyId", "WarehouseId", "ItemId"];
    private static readonly string[] KeyColumns074 = ["CompanyId", "DocumentType", "DocumentLineId"];
    private static readonly string[] KeyColumns075 = ["CompanyId", "SourceBranchId"];
    private static readonly string[] KeyColumns076 = ["CompanyId", "TargetBranchId"];
    private static readonly string[] KeyColumns077 = ["CompanyId", "TransferNumber"];
    private static readonly string[] KeyColumns078 = ["CompanyId", "ItemId", "FromUnitId", "ToUnitId"];
    private static readonly string[] KeyColumns079 = ["CompanyId", "PartyId"];
    private static readonly string[] KeyColumns080 = ["CompanyId", "ApprovedBy"];
    private static readonly string[] KeyColumns081 = ["CompanyId", "BranchId", "Kind", "VoucherDateUtc"];
    private static readonly string[] KeyColumns082 = ["CompanyId", "BranchId", "VoucherNumber"];
    private static readonly string[] KeyColumns083 = ["CompanyId", "BranchId", "IsActive"];
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "ledger");

        migrationBuilder.EnsureSchema(
            name: "audit");

        migrationBuilder.EnsureSchema(
            name: "treasury");

        migrationBuilder.EnsureSchema(
            name: "system");

        migrationBuilder.EnsureSchema(
            name: "sales");

        migrationBuilder.EnsureSchema(
            name: "inventory");

        migrationBuilder.EnsureSchema(
            name: "patterns");

        migrationBuilder.EnsureSchema(
            name: "purchases");

        migrationBuilder.EnsureSchema(
            name: "parties");

        migrationBuilder.CreateSequence(
            name: "MovementOrder",
            schema: "inventory");

        migrationBuilder.CreateTable(
            name: "Currencies",
            schema: "system",
            columns: table => new
            {
                Code = table.Column<string>(type: "char(3)", nullable: false, collation: "Latin1_General_100_BIN2"),
                NameArabic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                NameEnglish = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Currencies", x => x.Code);
                table.CheckConstraint("CK_Currencies_Code", "[Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z]%'");
                table.CheckConstraint("CK_Currencies_Names", "(DATALENGTH([NameArabic]) > 0 AND DATALENGTH([NameArabic]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [NameArabic]))) AND ([NameEnglish] IS NULL OR (DATALENGTH([NameEnglish]) > 0 AND DATALENGTH([NameEnglish]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [NameEnglish]))))");
                table.CheckConstraint("CK_Currencies_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
            });

        migrationBuilder.CreateTable(
            name: "Companies",
            schema: "system",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                NameArabic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                NameEnglish = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                BaseCurrencyCode = table.Column<string>(type: "char(3)", nullable: false, collation: "Latin1_General_100_BIN2"),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Companies", x => x.Id);
                table.CheckConstraint("CK_Companies_Code", "DATALENGTH([Code]) > 0 AND DATALENGTH([Code]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Code]))");
                table.CheckConstraint("CK_Companies_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_Companies_Names", "(DATALENGTH([NameArabic]) > 0 AND DATALENGTH([NameArabic]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [NameArabic]))) AND ([NameEnglish] IS NULL OR (DATALENGTH([NameEnglish]) > 0 AND DATALENGTH([NameEnglish]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [NameEnglish]))))");
                table.CheckConstraint("CK_Companies_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_Companies_Currencies_BaseCurrencyCode",
                    column: x => x.BaseCurrencyCode,
                    principalSchema: "system",
                    principalTable: "Currencies",
                    principalColumn: "Code");
            });

        migrationBuilder.CreateTable(
            name: "Accounts",
            schema: "ledger",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                NameArabic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                NameEnglish = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                AccountType = table.Column<byte>(type: "tinyint", nullable: false),
                PostingAllowed = table.Column<bool>(type: "bit", nullable: false),
                CostCenterPolicy = table.Column<byte>(type: "tinyint", nullable: false),
                CurrencyMode = table.Column<byte>(type: "tinyint", nullable: false),
                FixedCurrencyCode = table.Column<string>(type: "char(3)", nullable: true, collation: "Latin1_General_100_BIN2"),
                ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Accounts", x => x.Id);
                table.CheckConstraint("CK_Accounts_AccountType", "[AccountType] IN (1, 2, 3, 4, 5)");
                table.CheckConstraint("CK_Accounts_Code", "DATALENGTH([Code]) > 0 AND DATALENGTH([Code]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Code]))");
                table.CheckConstraint("CK_Accounts_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_Accounts_CostCenterPolicy", "[CostCenterPolicy] IN (1, 2, 3)");
                table.CheckConstraint("CK_Accounts_CurrencyMode", "[CurrencyMode] IN (1, 2, 3)");
                table.CheckConstraint("CK_Accounts_FixedCurrency", "([CurrencyMode] = 3 AND [FixedCurrencyCode] IS NOT NULL) OR ([CurrencyMode] <> 3 AND [FixedCurrencyCode] IS NULL)");
                table.CheckConstraint("CK_Accounts_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_Accounts_Names", "(DATALENGTH([NameArabic]) > 0 AND DATALENGTH([NameArabic]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [NameArabic]))) AND ([NameEnglish] IS NULL OR (DATALENGTH([NameEnglish]) > 0 AND DATALENGTH([NameEnglish]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [NameEnglish]))))");
                table.CheckConstraint("CK_Accounts_ParentNotSelf", "[ParentId] IS NULL OR [ParentId] <> [Id]");
                table.CheckConstraint("CK_Accounts_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_Accounts_Companies",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Accounts_FixedCurrency",
                    column: x => x.FixedCurrencyCode,
                    principalSchema: "system",
                    principalTable: "Currencies",
                    principalColumn: "Code",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Accounts_ParentAccount",
                    column: x => x.ParentId,
                    principalSchema: "ledger",
                    principalTable: "Accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "ActorReferences",
            schema: "audit",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DisplayNameArabic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                DisplayNameEnglish = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                FirstVerifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ActorReferences", x => x.Id);
                table.UniqueConstraint("AK_ActorReferences_CompanyId_Id", x => new { x.CompanyId, x.Id });
                table.CheckConstraint("CK_ActorReferences_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_ActorReferences_Name", "DATALENGTH([DisplayNameArabic]) > 0 AND DATALENGTH([DisplayNameArabic]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [DisplayNameArabic]))");
                table.CheckConstraint("CK_ActorReferences_Utc", "DATEPART(TZOFFSET, [FirstVerifiedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_ActorReferences_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Branches",
            schema: "system",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                NameArabic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                NameEnglish = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Branches", x => x.Id);
                table.UniqueConstraint("AK_Branches_CompanyId_Id", x => new { x.CompanyId, x.Id });
                table.CheckConstraint("CK_Branches_Code", "DATALENGTH([Code]) > 0 AND DATALENGTH([Code]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Code]))");
                table.CheckConstraint("CK_Branches_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_Branches_Names", "(DATALENGTH([NameArabic]) > 0 AND DATALENGTH([NameArabic]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [NameArabic]))) AND ([NameEnglish] IS NULL OR (DATALENGTH([NameEnglish]) > 0 AND DATALENGTH([NameEnglish]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [NameEnglish]))))");
                table.CheckConstraint("CK_Branches_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_Branches_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateTable(
            name: "CompanyAdditionalCurrencies",
            schema: "system",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CurrencyCode = table.Column<string>(type: "char(3)", nullable: false, collation: "Latin1_General_100_BIN2"),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CompanyAdditionalCurrencies", x => new { x.CompanyId, x.CurrencyCode });
                table.CheckConstraint("CK_CompanyAdditionalCurrencies_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_CompanyAdditionalCurrencies_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_CompanyAdditionalCurrencies_Currencies_CurrencyCode",
                    column: x => x.CurrencyCode,
                    principalSchema: "system",
                    principalTable: "Currencies",
                    principalColumn: "Code");
            });

        migrationBuilder.CreateTable(
            name: "CostCenters",
            schema: "ledger",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                NameArabic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                NameEnglish = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CostCenters", x => x.Id);
                table.CheckConstraint("CK_CostCenters_Code", "DATALENGTH([Code]) > 0 AND DATALENGTH([Code]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Code]))");
                table.CheckConstraint("CK_CostCenters_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_CostCenters_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_CostCenters_Names", "(DATALENGTH([NameArabic]) > 0 AND DATALENGTH([NameArabic]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [NameArabic]))) AND ([NameEnglish] IS NULL OR (DATALENGTH([NameEnglish]) > 0 AND DATALENGTH([NameEnglish]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [NameEnglish]))))");
                table.CheckConstraint("CK_CostCenters_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_CostCenters_Companies",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "DocumentPatterns",
            schema: "patterns",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OperationKind = table.Column<int>(type: "int", nullable: false),
                Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                NameArabic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                NameEnglish = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                ShortCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false, collation: "Latin1_General_100_BIN2"),
                Icon = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                Group = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                IsDefault = table.Column<bool>(type: "bit", nullable: false),
                ActiveRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DocumentPatterns", x => x.Id);
                table.CheckConstraint("CK_DocumentPatterns_Code", "DATALENGTH([Code]) > 0 AND DATALENGTH([Code]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Code]))");
                table.CheckConstraint("CK_DocumentPatterns_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_DocumentPatterns_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_DocumentPatterns_Names", "(DATALENGTH([NameArabic]) > 0 AND DATALENGTH([NameArabic]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [NameArabic]))) AND ([NameEnglish] IS NULL OR (DATALENGTH([NameEnglish]) > 0 AND DATALENGTH([NameEnglish]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [NameEnglish]))))");
                table.CheckConstraint("CK_DocumentPatterns_ShortCode", "DATALENGTH([ShortCode]) > 0 AND DATALENGTH([ShortCode]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [ShortCode]))");
                table.CheckConstraint("CK_DocumentPatterns_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_DocumentPatterns_Companies",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "FiscalYears",
            schema: "ledger",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                RangeStart = table.Column<DateOnly>(type: "date", nullable: false),
                RangeEnd = table.Column<DateOnly>(type: "date", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FiscalYears", x => x.Id);
                table.UniqueConstraint("AK_FiscalYears_CompanyId_Id", x => new { x.CompanyId, x.Id });
                table.CheckConstraint("CK_FiscalYears_Code", "DATALENGTH([Code]) > 0 AND DATALENGTH([Code]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Code]))");
                table.CheckConstraint("CK_FiscalYears_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_FiscalYears_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_FiscalYears_Range", "[RangeEnd] >= [RangeStart]");
                table.CheckConstraint("CK_FiscalYears_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_FiscalYears_Companies",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "ItemGroups",
            schema: "inventory",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                NameArabic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                NameEnglish = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ItemGroups", x => x.Id);
                table.CheckConstraint("CK_ItemGroups_Code", "DATALENGTH([Code]) > 0 AND DATALENGTH([Code]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Code]))");
                table.CheckConstraint("CK_ItemGroups_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_ItemGroups_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_ItemGroups_Names", "(DATALENGTH([NameArabic]) > 0 AND DATALENGTH([NameArabic]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [NameArabic]))) AND ([NameEnglish] IS NULL OR (DATALENGTH([NameEnglish]) > 0 AND DATALENGTH([NameEnglish]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [NameEnglish]))))");
                table.CheckConstraint("CK_ItemGroups_ParentSelf", "[ParentId] IS NULL OR [ParentId] <> [Id]");
                table.CheckConstraint("CK_ItemGroups_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_ItemGroups_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ItemGroups_ItemGroups_ParentId",
                    column: x => x.ParentId,
                    principalSchema: "inventory",
                    principalTable: "ItemGroups",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "UnitsOfMeasure",
            schema: "inventory",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                NameArabic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                NameEnglish = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                Symbol = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                DecimalPlaces = table.Column<int>(type: "int", nullable: false),
                Dimension = table.Column<byte>(type: "tinyint", nullable: false, defaultValue: (byte)0),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UnitsOfMeasure", x => x.Id);
                table.CheckConstraint("CK_UnitsOfMeasure_Code", "DATALENGTH([Code]) > 0 AND DATALENGTH([Code]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Code]))");
                table.CheckConstraint("CK_UnitsOfMeasure_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_UnitsOfMeasure_DecimalPlaces", "[DecimalPlaces] BETWEEN 0 AND 4");
                table.CheckConstraint("CK_UnitsOfMeasure_Dimension", "[Dimension] BETWEEN 0 AND 7");
                table.CheckConstraint("CK_UnitsOfMeasure_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_UnitsOfMeasure_Names", "(DATALENGTH([NameArabic]) > 0 AND DATALENGTH([NameArabic]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [NameArabic]))) AND ([NameEnglish] IS NULL OR (DATALENGTH([NameEnglish]) > 0 AND DATALENGTH([NameEnglish]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [NameEnglish]))))");
                table.CheckConstraint("CK_UnitsOfMeasure_Symbol", "DATALENGTH([Symbol]) > 0 AND DATALENGTH([Symbol]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Symbol]))");
                table.CheckConstraint("CK_UnitsOfMeasure_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_UnitsOfMeasure_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Parties",
            schema: "parties",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                NameArabic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                NameEnglish = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                NormalizedSearchText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                Role = table.Column<int>(type: "int", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                IsCashCustomer = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                IsCreditBlocked = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                TaxNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                CommercialRegistration = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                DefaultCurrencyCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true, collation: "Latin1_General_100_BIN2"),
                ReceivableAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                PayableAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                CreditLimit = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                PaymentTermDays = table.Column<int>(type: "int", nullable: false),
                CreditPolicyKind = table.Column<int>(type: "int", nullable: false),
                ContactsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                AddressesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Parties", x => x.Id);
                table.CheckConstraint("CK_Parties_Code", "DATALENGTH([Code]) > 0 AND DATALENGTH([Code]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Code]))");
                table.CheckConstraint("CK_Parties_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_Parties_CreditLimit", "[CreditLimit] >= 0");
                table.CheckConstraint("CK_Parties_CreditPolicyKind", "[CreditPolicyKind] IN (1, 2, 3)");
                table.CheckConstraint("CK_Parties_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_Parties_Names", "(DATALENGTH([NameArabic]) > 0 AND DATALENGTH([NameArabic]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [NameArabic]))) AND ([NameEnglish] IS NULL OR (DATALENGTH([NameEnglish]) > 0 AND DATALENGTH([NameEnglish]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [NameEnglish]))))");
                table.CheckConstraint("CK_Parties_PaymentTermDays", "[PaymentTermDays] >= 0");
                table.CheckConstraint("CK_Parties_Role", "[Role] IN (1, 2, 3)");
                table.CheckConstraint("CK_Parties_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_Parties_Companies",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Parties_PayableAccount",
                    column: x => x.PayableAccountId,
                    principalSchema: "ledger",
                    principalTable: "Accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Parties_ReceivableAccount",
                    column: x => x.ReceivableAccountId,
                    principalSchema: "ledger",
                    principalTable: "Accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "CurrencyAudit",
            schema: "system",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Action = table.Column<byte>(type: "tinyint", nullable: false),
                CurrencyCode = table.Column<string>(type: "char(3)", nullable: false, collation: "Latin1_General_100_BIN2"),
                PreviousCurrencyCode = table.Column<string>(type: "char(3)", nullable: true, collation: "Latin1_General_100_BIN2"),
                Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CurrencyAudit", x => x.Id);
                table.CheckConstraint("CK_CurrencyAudit_Action", "[Action] IN (1, 2)");
                table.CheckConstraint("CK_CurrencyAudit_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_CurrencyAudit_Reason", "DATALENGTH([Reason]) > 0 AND DATALENGTH([Reason]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Reason]))");
                table.CheckConstraint("CK_CurrencyAudit_Utc", "DATEPART(TZOFFSET, [OccurredAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_CurrencyAudit_ActorReferences_CompanyId_ActorUserId",
                    columns: x => new { x.CompanyId, x.ActorUserId },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CurrencyAudit_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CurrencyAudit_Currencies_CurrencyCode",
                    column: x => x.CurrencyCode,
                    principalSchema: "system",
                    principalTable: "Currencies",
                    principalColumn: "Code",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CurrencyAudit_Currencies_PreviousCurrencyCode",
                    column: x => x.PreviousCurrencyCode,
                    principalSchema: "system",
                    principalTable: "Currencies",
                    principalColumn: "Code",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "ExchangeRates",
            schema: "system",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CurrencyCode = table.Column<string>(type: "char(3)", nullable: false, collation: "Latin1_General_100_BIN2"),
                BaseCurrencyCode = table.Column<string>(type: "char(3)", nullable: false, collation: "Latin1_General_100_BIN2"),
                Rate = table.Column<decimal>(type: "decimal(19,8)", precision: 19, scale: 8, nullable: false),
                EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                Source = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ExchangeRates", x => x.Id);
                table.CheckConstraint("CK_ExchangeRates_Currencies", "[CurrencyCode] <> [BaseCurrencyCode]");
                table.CheckConstraint("CK_ExchangeRates_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_ExchangeRates_Rate", "[Rate] > 0");
                table.CheckConstraint("CK_ExchangeRates_Reason", "DATALENGTH([Reason]) > 0 AND DATALENGTH([Reason]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Reason]))");
                table.CheckConstraint("CK_ExchangeRates_Source", "DATALENGTH([Source]) > 0 AND DATALENGTH([Source]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Source]))");
                table.CheckConstraint("CK_ExchangeRates_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_ExchangeRates_ActorReferences_CompanyId_CreatedByUserId",
                    columns: x => new { x.CompanyId, x.CreatedByUserId },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ExchangeRates_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ExchangeRates_Currencies_BaseCurrencyCode",
                    column: x => x.BaseCurrencyCode,
                    principalSchema: "system",
                    principalTable: "Currencies",
                    principalColumn: "Code",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ExchangeRates_Currencies_CurrencyCode",
                    column: x => x.CurrencyCode,
                    principalSchema: "system",
                    principalTable: "Currencies",
                    principalColumn: "Code",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "PostingRequests",
            schema: "ledger",
            columns: table => new
            {
                IdempotencyKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Operation = table.Column<byte>(type: "tinyint", nullable: false),
                ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                JournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ExpectedRevision = table.Column<long>(type: "bigint", nullable: false),
                ExpectedVersionBase64 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                OriginalJournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                OriginalExpectedRevision = table.Column<long>(type: "bigint", nullable: true),
                OriginalVersionBase64 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                FingerprintHex = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                RegisteredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                OutcomeJournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PostingRequests", x => new { x.CompanyId, x.BranchId, x.Operation, x.IdempotencyKey });
                table.CheckConstraint("CK_PostingRequests_ActorUserId", "[ActorUserId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_PostingRequests_BranchId", "[BranchId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_PostingRequests_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_PostingRequests_CompletedAtUtc", "[CompletedAtUtc] IS NULL OR DATEPART(TZOFFSET, [CompletedAtUtc]) = 0");
                table.CheckConstraint("CK_PostingRequests_ExpectedRevision", "[ExpectedRevision] > 0");
                table.CheckConstraint("CK_PostingRequests_FingerprintHex", "LEN([FingerprintHex]) = 64");
                table.CheckConstraint("CK_PostingRequests_IdempotencyKey", "[IdempotencyKey] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_PostingRequests_JournalId", "[JournalId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_PostingRequests_Operation", "[Operation] IN (1, 2)");
                table.CheckConstraint("CK_PostingRequests_Reason", "DATALENGTH([Reason]) > 0 AND DATALENGTH([Reason]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Reason]))");
                table.CheckConstraint("CK_PostingRequests_RegisteredAtUtc", "DATEPART(TZOFFSET, [RegisteredAtUtc]) = 0");
                table.CheckConstraint("CK_PostingRequests_ReversalConsistency", "([Operation] = 2 AND [OriginalJournalId] IS NOT NULL AND [OriginalJournalId] <> '00000000-0000-0000-0000-000000000000') OR ([Operation] = 1 AND [OriginalJournalId] IS NULL)");
                table.ForeignKey(
                    name: "FK_PostingRequests_ActorReferences_CompanyId_ActorUserId",
                    columns: x => new { x.CompanyId, x.ActorUserId },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "AccountAudit",
            schema: "ledger",
            columns: table => new
            {
                EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Action = table.Column<byte>(type: "tinyint", nullable: false),
                Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                NameArabic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                NameEnglish = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                AccountType = table.Column<byte>(type: "tinyint", nullable: false),
                ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkingBranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AccountAudit", x => x.EventId);
                table.CheckConstraint("CK_AccountAudit_AccountId", "[AccountId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_AccountAudit_AccountType", "[AccountType] BETWEEN 1 AND 5");
                table.CheckConstraint("CK_AccountAudit_Action", "[Action] IN (1, 2, 3, 4, 5)");
                table.CheckConstraint("CK_AccountAudit_ActorUserId", "[ActorUserId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_AccountAudit_CorrelationId", "[CorrelationId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_AccountAudit_EventId", "[EventId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_AccountAudit_Reason", "([Reason] IS NULL OR (DATALENGTH([Reason]) > 0 AND DATALENGTH([Reason]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Reason])))) AND ([Action] <> 1 OR [Reason] IS NOT NULL)");
                table.CheckConstraint("CK_AccountAudit_Utc", "DATEPART(TZOFFSET, [OccurredAtUtc]) = 0 AND DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_AccountAudit_ActorReferences_CompanyId_ActorUserId",
                    columns: x => new { x.CompanyId, x.ActorUserId },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AccountAudit_Branches_CompanyId_WorkingBranchId",
                    columns: x => new { x.CompanyId, x.WorkingBranchId },
                    principalSchema: "system",
                    principalTable: "Branches",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AccountAudit_Companies",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "CostCenterAudit",
            schema: "ledger",
            columns: table => new
            {
                EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Action = table.Column<byte>(type: "tinyint", nullable: false),
                ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkingBranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CostCenterAudit", x => x.EventId);
                table.CheckConstraint("CK_CostCenterAudit_Action", "[Action] IN (1,2,3,4)");
                table.CheckConstraint("CK_CostCenterAudit_Ids", "[EventId] <> '00000000-0000-0000-0000-000000000000' AND [CostCenterId] <> '00000000-0000-0000-0000-000000000000' AND [CorrelationId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_CostCenterAudit_Utc", "DATEPART(TZOFFSET,[OccurredAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_CostCenterAudit_ActorReferences_CompanyId_ActorUserId",
                    columns: x => new { x.CompanyId, x.ActorUserId },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CostCenterAudit_Branches_CompanyId_WorkingBranchId",
                    columns: x => new { x.CompanyId, x.WorkingBranchId },
                    principalSchema: "system",
                    principalTable: "Branches",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CostCenterAudit_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "DocumentSequences",
            schema: "inventory",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Year = table.Column<int>(type: "int", nullable: false),
                Kind = table.Column<int>(type: "int", nullable: false),
                NextValue = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DocumentSequences", x => new { x.CompanyId, x.BranchId, x.Year, x.Kind });
                table.CheckConstraint("CK_InventorySequence_Value", "[NextValue]>=0");
                table.ForeignKey(
                    name: "FK_DocumentSequences_Branches_CompanyId_BranchId",
                    columns: x => new { x.CompanyId, x.BranchId },
                    principalSchema: "system",
                    principalTable: "Branches",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_DocumentSequences_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Warehouses",
            schema: "inventory",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                NameArabic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                NameEnglish = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                Location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Warehouses", x => x.Id);
                table.CheckConstraint("CK_Warehouses_BranchId", "[BranchId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_Warehouses_Code", "DATALENGTH([Code]) > 0 AND DATALENGTH([Code]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Code]))");
                table.CheckConstraint("CK_Warehouses_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_Warehouses_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_Warehouses_Names", "(DATALENGTH([NameArabic]) > 0 AND DATALENGTH([NameArabic]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [NameArabic]))) AND ([NameEnglish] IS NULL OR (DATALENGTH([NameEnglish]) > 0 AND DATALENGTH([NameEnglish]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [NameEnglish]))))");
                table.CheckConstraint("CK_Warehouses_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_Warehouses_Branches_CompanyId_BranchId",
                    columns: x => new { x.CompanyId, x.BranchId },
                    principalSchema: "system",
                    principalTable: "Branches",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Warehouses_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "PatternBranchGrants",
            schema: "patterns",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PatternId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PatternBranchGrants", x => x.Id);
                table.CheckConstraint("CK_PatternBranchGrants_BranchId", "[BranchId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_PatternBranchGrants_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_PatternBranchGrants_PatternId", "[PatternId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_PatternBranchGrants_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_PatternBranchGrants_Branches",
                    column: x => x.BranchId,
                    principalSchema: "system",
                    principalTable: "Branches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PatternBranchGrants_DocumentPatterns",
                    column: x => x.PatternId,
                    principalSchema: "patterns",
                    principalTable: "DocumentPatterns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "PatternNumberSequences",
            schema: "patterns",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PatternId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                FiscalYearId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LastSequence = table.Column<long>(type: "bigint", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PatternNumberSequences", x => new { x.CompanyId, x.BranchId, x.PatternId, x.FiscalYearId });
                table.CheckConstraint("CK_PatternNumberSequences_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_PatternNumberSequences_LastSequence", "[LastSequence] >= 0");
                table.CheckConstraint("CK_PatternNumberSequences_PatternId", "[PatternId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_PatternNumberSequences_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_PatternNumberSequences_Companies",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PatternNumberSequences_DocumentPatterns",
                    column: x => x.PatternId,
                    principalSchema: "patterns",
                    principalTable: "DocumentPatterns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "PatternRevisions",
            schema: "patterns",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PatternId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RevisionNumber = table.Column<int>(type: "int", nullable: false),
                State = table.Column<int>(type: "int", nullable: false),
                EffectiveFromUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                EffectiveToUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                ConfigurationJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PatternRevisions", x => x.Id);
                table.CheckConstraint("CK_PatternRevisions_CreatedByUserId", "[CreatedByUserId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_PatternRevisions_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_PatternRevisions_PatternId", "[PatternId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_PatternRevisions_Reason", "DATALENGTH([Reason]) > 0 AND DATALENGTH([Reason]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Reason]))");
                table.CheckConstraint("CK_PatternRevisions_RevisionNumber", "[RevisionNumber] > 0");
                table.CheckConstraint("CK_PatternRevisions_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_PatternRevisions_ActorReferences_CreatedByUserId",
                    column: x => x.CreatedByUserId,
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PatternRevisions_DocumentPatterns",
                    column: x => x.PatternId,
                    principalSchema: "patterns",
                    principalTable: "DocumentPatterns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AccountingPeriods",
            schema: "ledger",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                FiscalYearId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                RangeStart = table.Column<DateOnly>(type: "date", nullable: false),
                RangeEnd = table.Column<DateOnly>(type: "date", nullable: false),
                State = table.Column<byte>(type: "tinyint", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AccountingPeriods", x => x.Id);
                table.UniqueConstraint("AK_AccountingPeriods_CompanyId_FiscalYearId_Id", x => new { x.CompanyId, x.FiscalYearId, x.Id });
                table.CheckConstraint("CK_AccountingPeriods_Code", "DATALENGTH([Code]) > 0 AND DATALENGTH([Code]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Code]))");
                table.CheckConstraint("CK_AccountingPeriods_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_AccountingPeriods_FiscalYearId", "[FiscalYearId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_AccountingPeriods_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_AccountingPeriods_Range", "[RangeEnd] >= [RangeStart]");
                table.CheckConstraint("CK_AccountingPeriods_State", "[State] IN (0, 1, 2)");
                table.CheckConstraint("CK_AccountingPeriods_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_AccountingPeriods_Companies",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AccountingPeriods_FiscalYears_CompanyId_FiscalYearId",
                    columns: x => new { x.CompanyId, x.FiscalYearId },
                    principalSchema: "ledger",
                    principalTable: "FiscalYears",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "PostingSequences",
            schema: "ledger",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                FiscalYearId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LastSequence = table.Column<long>(type: "bigint", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PostingSequences", x => new { x.CompanyId, x.BranchId, x.FiscalYearId });
                table.CheckConstraint("CK_PostingSequences_BranchId", "[BranchId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_PostingSequences_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_PostingSequences_FiscalYearId", "[FiscalYearId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_PostingSequences_LastSequence", "[LastSequence] >= 0");
                table.CheckConstraint("CK_PostingSequences_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_PostingSequences_Branches_CompanyId_BranchId",
                    columns: x => new { x.CompanyId, x.BranchId },
                    principalSchema: "system",
                    principalTable: "Branches",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PostingSequences_Companies",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PostingSequences_FiscalYears_CompanyId_FiscalYearId",
                    columns: x => new { x.CompanyId, x.FiscalYearId },
                    principalSchema: "ledger",
                    principalTable: "FiscalYears",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "PolicyAudit",
            schema: "parties",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BeforeCreditLimit = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                AfterCreditLimit = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                BeforePolicy = table.Column<int>(type: "int", nullable: false),
                AfterPolicy = table.Column<int>(type: "int", nullable: false),
                BeforeBlocked = table.Column<bool>(type: "bit", nullable: false),
                AfterBlocked = table.Column<bool>(type: "bit", nullable: false),
                DateUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PolicyAudit", x => x.Id);
                table.CheckConstraint("CK_PartyPolicyAudit_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_PartyPolicyAudit_Limits", "[BeforeCreditLimit]>=0 AND [AfterCreditLimit]>=0");
                table.CheckConstraint("CK_PartyPolicyAudit_Policies", "[BeforePolicy] IN (1,2,3) AND [AfterPolicy] IN (1,2,3)");
                table.CheckConstraint("CK_PartyPolicyAudit_Utc", "DATEPART(TZOFFSET,[DateUtc])=0");
                table.ForeignKey(
                    name: "FK_PolicyAudit_ActorReferences_CompanyId_ActorId",
                    columns: x => new { x.CompanyId, x.ActorId },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PolicyAudit_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PolicyAudit_Parties_PartyId",
                    column: x => x.PartyId,
                    principalSchema: "parties",
                    principalTable: "Parties",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "AccountAuditStates",
            schema: "ledger",
            columns: table => new
            {
                EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Stage = table.Column<byte>(type: "tinyint", nullable: false),
                Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                NameArabic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                NameEnglish = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                AccountType = table.Column<byte>(type: "tinyint", nullable: false),
                PostingAllowed = table.Column<bool>(type: "bit", nullable: false),
                CostCenterPolicy = table.Column<byte>(type: "tinyint", nullable: false),
                CurrencyMode = table.Column<byte>(type: "tinyint", nullable: false),
                FixedCurrencyCode = table.Column<string>(type: "char(3)", nullable: true, collation: "Latin1_General_100_BIN2"),
                ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                IsActive = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AccountAuditStates", x => new { x.EventId, x.Stage });
                table.CheckConstraint("CK_AccountAuditStates_Code", "DATALENGTH([Code]) > 0 AND DATALENGTH([Code]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Code]))");
                table.CheckConstraint("CK_AccountAuditStates_Currency", "([CurrencyMode] = 3 AND [FixedCurrencyCode] IS NOT NULL) OR ([CurrencyMode] <> 3 AND [FixedCurrencyCode] IS NULL)");
                table.CheckConstraint("CK_AccountAuditStates_Names", "(DATALENGTH([NameArabic]) > 0 AND DATALENGTH([NameArabic]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [NameArabic]))) AND ([NameEnglish] IS NULL OR (DATALENGTH([NameEnglish]) > 0 AND DATALENGTH([NameEnglish]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [NameEnglish]))))");
                table.CheckConstraint("CK_AccountAuditStates_Stage", "[Stage] IN (1, 2)");
                table.CheckConstraint("CK_AccountAuditStates_Types", "[AccountType] BETWEEN 1 AND 5 AND [CostCenterPolicy] BETWEEN 1 AND 3 AND [CurrencyMode] BETWEEN 1 AND 3");
                table.ForeignKey(
                    name: "FK_AccountAuditStates_AccountAudit_EventId",
                    column: x => x.EventId,
                    principalSchema: "ledger",
                    principalTable: "AccountAudit",
                    principalColumn: "EventId",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "CostCenterAuditStates",
            schema: "ledger",
            columns: table => new
            {
                EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Stage = table.Column<byte>(type: "tinyint", nullable: false),
                Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                NameArabic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                NameEnglish = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                IsActive = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CostCenterAuditStates", x => new { x.EventId, x.Stage });
                table.CheckConstraint("CK_CostCenterAuditStates_Code", "DATALENGTH([Code]) > 0 AND DATALENGTH([Code]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Code]))");
                table.CheckConstraint("CK_CostCenterAuditStates_Names", "(DATALENGTH([NameArabic]) > 0 AND DATALENGTH([NameArabic]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [NameArabic]))) AND ([NameEnglish] IS NULL OR (DATALENGTH([NameEnglish]) > 0 AND DATALENGTH([NameEnglish]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [NameEnglish]))))");
                table.CheckConstraint("CK_CostCenterAuditStates_Stage", "[Stage] IN (1,2)");
                table.ForeignKey(
                    name: "FK_CostCenterAuditStates_CostCenterAudit_EventId",
                    column: x => x.EventId,
                    principalSchema: "ledger",
                    principalTable: "CostCenterAudit",
                    principalColumn: "EventId",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Items",
            schema: "inventory",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                NameArabic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                NameEnglish = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                NormalizedSearchText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                BaseUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DefaultWarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                CostMethod = table.Column<int>(type: "int", nullable: false),
                MinStock = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                MaxStock = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                ReorderPoint = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Items", x => x.Id);
                table.CheckConstraint("CK_Items_Code", "DATALENGTH([Code]) > 0 AND DATALENGTH([Code]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Code]))");
                table.CheckConstraint("CK_Items_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_Items_CostMethod", "[CostMethod] IN (1, 2)");
                table.CheckConstraint("CK_Items_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_Items_MaxStock", "[MaxStock] IS NULL OR [MaxStock] >= 0");
                table.CheckConstraint("CK_Items_MinStock", "[MinStock] IS NULL OR [MinStock] >= 0");
                table.CheckConstraint("CK_Items_Names", "(DATALENGTH([NameArabic]) > 0 AND DATALENGTH([NameArabic]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [NameArabic]))) AND ([NameEnglish] IS NULL OR (DATALENGTH([NameEnglish]) > 0 AND DATALENGTH([NameEnglish]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [NameEnglish]))))");
                table.CheckConstraint("CK_Items_ReorderPoint", "[ReorderPoint] IS NULL OR [ReorderPoint] >= 0");
                table.CheckConstraint("CK_Items_StockLevels", "([MinStock] IS NULL OR [MaxStock] IS NULL) OR ([MinStock] <= [MaxStock])");
                table.CheckConstraint("CK_Items_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_Items_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Items_ItemGroups_GroupId",
                    column: x => x.GroupId,
                    principalSchema: "inventory",
                    principalTable: "ItemGroups",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Items_UnitsOfMeasure_BaseUnitId",
                    column: x => x.BaseUnitId,
                    principalSchema: "inventory",
                    principalTable: "UnitsOfMeasure",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Items_Warehouses_DefaultWarehouseId",
                    column: x => x.DefaultWarehouseId,
                    principalSchema: "inventory",
                    principalTable: "Warehouses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "StockAdjustments",
            schema: "inventory",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AdjustmentNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                AdjustmentDateUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StockAdjustments", x => x.Id);
                table.CheckConstraint("CK_StockAdjustments_AdjustmentDateUtc", "DATEPART(TZOFFSET, [AdjustmentDateUtc]) = 0");
                table.CheckConstraint("CK_StockAdjustments_AdjustmentNumber", "DATALENGTH([AdjustmentNumber]) > 0 AND DATALENGTH([AdjustmentNumber]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [AdjustmentNumber]))");
                table.CheckConstraint("CK_StockAdjustments_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_StockAdjustments_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_StockAdjustments_Reason", "DATALENGTH([Reason]) > 0 AND DATALENGTH([Reason]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Reason]))");
                table.CheckConstraint("CK_StockAdjustments_Status", "[Status] IN (1, 2)");
                table.CheckConstraint("CK_StockAdjustments_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_StockAdjustments_ActorReferences_CompanyId_ApprovedByUserId",
                    columns: x => new { x.CompanyId, x.ApprovedByUserId },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_StockAdjustments_ActorReferences_CompanyId_CreatedByUserId",
                    columns: x => new { x.CompanyId, x.CreatedByUserId },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_StockAdjustments_Branches_CompanyId_BranchId",
                    columns: x => new { x.CompanyId, x.BranchId },
                    principalSchema: "system",
                    principalTable: "Branches",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_StockAdjustments_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_StockAdjustments_Warehouses_WarehouseId",
                    column: x => x.WarehouseId,
                    principalSchema: "inventory",
                    principalTable: "Warehouses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "StockTransfers",
            schema: "inventory",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TransferNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                SourceBranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SourceWarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TargetBranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TargetWarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TransferDateUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StockTransfers", x => x.Id);
                table.CheckConstraint("CK_StockTransfers_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_StockTransfers_DistinctWarehouses", "[SourceWarehouseId] <> [TargetWarehouseId]");
                table.CheckConstraint("CK_StockTransfers_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_StockTransfers_Status", "[Status] IN (1, 2, 3)");
                table.CheckConstraint("CK_StockTransfers_TransferDateUtc", "DATEPART(TZOFFSET, [TransferDateUtc]) = 0");
                table.CheckConstraint("CK_StockTransfers_TransferNumber", "DATALENGTH([TransferNumber]) > 0 AND DATALENGTH([TransferNumber]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [TransferNumber]))");
                table.CheckConstraint("CK_StockTransfers_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_StockTransfers_ActorReferences_CompanyId_CreatedByUserId",
                    columns: x => new { x.CompanyId, x.CreatedByUserId },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_StockTransfers_Branches_CompanyId_SourceBranchId",
                    columns: x => new { x.CompanyId, x.SourceBranchId },
                    principalSchema: "system",
                    principalTable: "Branches",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_StockTransfers_Branches_CompanyId_TargetBranchId",
                    columns: x => new { x.CompanyId, x.TargetBranchId },
                    principalSchema: "system",
                    principalTable: "Branches",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_StockTransfers_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_StockTransfers_Warehouses_SourceWarehouseId",
                    column: x => x.SourceWarehouseId,
                    principalSchema: "inventory",
                    principalTable: "Warehouses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_StockTransfers_Warehouses_TargetWarehouseId",
                    column: x => x.TargetWarehouseId,
                    principalSchema: "inventory",
                    principalTable: "Warehouses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "JournalEntries",
            schema: "ledger",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AccountingDate = table.Column<DateOnly>(type: "date", nullable: false),
                Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                State = table.Column<byte>(type: "tinyint", nullable: false),
                PostingSequence = table.Column<long>(type: "bigint", nullable: true),
                PostingFiscalYearId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                PostingPeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ReversesEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ReversedByEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                CorrectsEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_JournalEntries", x => x.Id);
                table.CheckConstraint("CK_JournalEntries_BranchId", "[BranchId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_JournalEntries_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_JournalEntries_Description", "DATALENGTH([Description]) > 0 AND DATALENGTH([Description]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Description]))");
                table.CheckConstraint("CK_JournalEntries_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_JournalEntries_NotSelfCorrected", "[CorrectsEntryId] IS NULL OR [CorrectsEntryId] <> [Id]");
                table.CheckConstraint("CK_JournalEntries_NotSelfReversal", "[ReversesEntryId] IS NULL OR [ReversesEntryId] <> [Id]");
                table.CheckConstraint("CK_JournalEntries_PostedState", "([State] IN (4, 5) AND [PostingSequence] IS NOT NULL) OR ([State] NOT IN (4, 5) AND [PostingSequence] IS NULL)");
                table.CheckConstraint("CK_JournalEntries_PostingConsistency", "([PostingSequence] IS NULL AND [PostingFiscalYearId] IS NULL AND [PostingPeriodId] IS NULL) OR ([PostingSequence] IS NOT NULL AND [PostingSequence] > 0 AND [PostingFiscalYearId] IS NOT NULL AND [PostingPeriodId] IS NOT NULL)");
                table.CheckConstraint("CK_JournalEntries_State", "[State] IN (1, 2, 3, 4, 5)");
                table.CheckConstraint("CK_JournalEntries_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_JournalEntries_AccountingPeriods_CompanyId_PostingFiscalYearId_PostingPeriodId",
                    columns: x => new { x.CompanyId, x.PostingFiscalYearId, x.PostingPeriodId },
                    principalSchema: "ledger",
                    principalTable: "AccountingPeriods",
                    principalColumns: KeyColumns002,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_JournalEntries_Branches_CompanyId_BranchId",
                    columns: x => new { x.CompanyId, x.BranchId },
                    principalSchema: "system",
                    principalTable: "Branches",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_JournalEntries_Companies",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "PeriodAudit",
            schema: "ledger",
            columns: table => new
            {
                EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                FiscalYearId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PreviousState = table.Column<byte>(type: "tinyint", nullable: false),
                NewState = table.Column<byte>(type: "tinyint", nullable: false),
                ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PeriodAudit", x => x.EventId);
                table.CheckConstraint("CK_PeriodAudit_ActorUserId", "[ActorUserId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_PeriodAudit_CorrelationId", "[CorrelationId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_PeriodAudit_EventId", "[EventId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_PeriodAudit_Reason", "DATALENGTH([Reason]) > 0 AND DATALENGTH([Reason]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Reason]))");
                table.CheckConstraint("CK_PeriodAudit_States", "[PreviousState] IN (0, 1, 2) AND [NewState] IN (0, 1, 2) AND [PreviousState] <> [NewState]");
                table.CheckConstraint("CK_PeriodAudit_Utc", "DATEPART(TZOFFSET, [OccurredAtUtc]) = 0 AND DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_PeriodAudit_AccountingPeriods_CompanyId_FiscalYearId_PeriodId",
                    columns: x => new { x.CompanyId, x.FiscalYearId, x.PeriodId },
                    principalSchema: "ledger",
                    principalTable: "AccountingPeriods",
                    principalColumns: KeyColumns002,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PeriodAudit_ActorReferences_CompanyId_ActorUserId",
                    columns: x => new { x.CompanyId, x.ActorUserId },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PeriodAudit_Companies",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PeriodAudit_FiscalYears_CompanyId_FiscalYearId",
                    columns: x => new { x.CompanyId, x.FiscalYearId },
                    principalSchema: "ledger",
                    principalTable: "FiscalYears",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "ItemBarcodes",
            schema: "inventory",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Barcode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                IsDefault = table.Column<bool>(type: "bit", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ItemBarcodes", x => x.Id);
                table.CheckConstraint("CK_ItemBarcodes_Barcode", "DATALENGTH([Barcode]) > 0 AND DATALENGTH([Barcode]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Barcode]))");
                table.CheckConstraint("CK_ItemBarcodes_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_ItemBarcodes_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_ItemBarcodes_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_ItemBarcodes_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ItemBarcodes_Items_ItemId",
                    column: x => x.ItemId,
                    principalSchema: "inventory",
                    principalTable: "Items",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ItemBarcodes_UnitsOfMeasure_UnitId",
                    column: x => x.UnitId,
                    principalSchema: "inventory",
                    principalTable: "UnitsOfMeasure",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "ItemPrices",
            schema: "inventory",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PriceListCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                Price = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                CurrencyCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false, collation: "Latin1_General_100_BIN2"),
                MinQuantity = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                PatternId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ValidFrom = table.Column<DateOnly>(type: "date", nullable: true),
                ValidTo = table.Column<DateOnly>(type: "date", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ItemPrices", x => x.Id);
                table.CheckConstraint("CK_ItemPrices_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_ItemPrices_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_ItemPrices_MinQuantity", "[MinQuantity] >= 0");
                table.CheckConstraint("CK_ItemPrices_Price", "[Price] >= 0");
                table.CheckConstraint("CK_ItemPrices_PriceListCode", "DATALENGTH([PriceListCode]) > 0 AND DATALENGTH([PriceListCode]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [PriceListCode]))");
                table.CheckConstraint("CK_ItemPrices_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                table.CheckConstraint("CK_ItemPrices_Validity", "[ValidFrom] IS NULL OR [ValidTo] IS NULL OR [ValidTo] >= [ValidFrom]");
                table.ForeignKey(
                    name: "FK_ItemPrices_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ItemPrices_DocumentPatterns_PatternId",
                    column: x => x.PatternId,
                    principalSchema: "patterns",
                    principalTable: "DocumentPatterns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ItemPrices_Items_ItemId",
                    column: x => x.ItemId,
                    principalSchema: "inventory",
                    principalTable: "Items",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ItemPrices_Parties_PartyId",
                    column: x => x.PartyId,
                    principalSchema: "parties",
                    principalTable: "Parties",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ItemPrices_UnitsOfMeasure_UnitId",
                    column: x => x.UnitId,
                    principalSchema: "inventory",
                    principalTable: "UnitsOfMeasure",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "StockMovements",
            schema: "inventory",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                MovementKind = table.Column<int>(type: "int", nullable: false),
                Quantity = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                BaseQuantity = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                UnitCost = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                TotalCost = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                MovementDateUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                DocumentType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                DocumentLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                DocumentNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                CostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RecordedSequence = table.Column<long>(type: "bigint", nullable: false),
                RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                QuantityBefore = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                QuantityAfter = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                ValueBefore = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                ValueAfter = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                ReversesMovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StockMovements", x => x.Id);
                table.CheckConstraint("CK_StockMovements_AuditBalance", "([QuantityBefore] IS NULL AND [QuantityAfter] IS NULL AND [ValueBefore] IS NULL AND [ValueAfter] IS NULL) OR ([QuantityBefore] IS NOT NULL AND [QuantityAfter] IS NOT NULL AND [ValueBefore] IS NOT NULL AND [ValueAfter] IS NOT NULL AND [QuantityBefore]>=0 AND [QuantityAfter]>=0 AND [ValueBefore]>=0 AND [ValueAfter]>=0 AND [QuantityAfter]=[QuantityBefore]+[BaseQuantity] AND [ValueAfter]=[ValueBefore]+CASE WHEN [BaseQuantity]<0 THEN -[TotalCost] ELSE [TotalCost] END AND [RecordedAtUtc] IS NOT NULL)");
                table.CheckConstraint("CK_StockMovements_BaseQuantity", "[BaseQuantity] <> 0");
                table.CheckConstraint("CK_StockMovements_BranchId", "[BranchId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_StockMovements_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_StockMovements_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_StockMovements_InternalIssueCostCenter", "([MovementKind] <> 10) OR ([CostCenterId] IS NOT NULL)");
                table.CheckConstraint("CK_StockMovements_ItemId", "[ItemId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_StockMovements_MovementKind", "[MovementKind] BETWEEN 1 AND 10");
                table.CheckConstraint("CK_StockMovements_Quantity", "[Quantity] > 0");
                table.CheckConstraint("CK_StockMovements_TotalCost", "[TotalCost] >= 0");
                table.CheckConstraint("CK_StockMovements_UnitCost", "[UnitCost] >= 0");
                table.CheckConstraint("CK_StockMovements_UnitId", "[UnitId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_StockMovements_Utc", "DATEPART(TZOFFSET, [MovementDateUtc]) = 0 AND DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                table.CheckConstraint("CK_StockMovements_WarehouseId", "[WarehouseId] <> '00000000-0000-0000-0000-000000000000'");
                table.ForeignKey(
                    name: "FK_StockMovements_ActorReferences_CompanyId_CreatedByUserId",
                    columns: x => new { x.CompanyId, x.CreatedByUserId },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_StockMovements_Branches_CompanyId_BranchId",
                    columns: x => new { x.CompanyId, x.BranchId },
                    principalSchema: "system",
                    principalTable: "Branches",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_StockMovements_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_StockMovements_CostCenters_CostCenterId",
                    column: x => x.CostCenterId,
                    principalSchema: "ledger",
                    principalTable: "CostCenters",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_StockMovements_Items_ItemId",
                    column: x => x.ItemId,
                    principalSchema: "inventory",
                    principalTable: "Items",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_StockMovements_StockMovements_ReversesMovementId",
                    column: x => x.ReversesMovementId,
                    principalSchema: "inventory",
                    principalTable: "StockMovements",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_StockMovements_UnitsOfMeasure_UnitId",
                    column: x => x.UnitId,
                    principalSchema: "inventory",
                    principalTable: "UnitsOfMeasure",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_StockMovements_Warehouses_WarehouseId",
                    column: x => x.WarehouseId,
                    principalSchema: "inventory",
                    principalTable: "Warehouses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "UnitConversions",
            schema: "inventory",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                FromUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ToUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Factor = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UnitConversions", x => x.Id);
                table.CheckConstraint("CK_UnitConversions_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_UnitConversions_DistinctUnits", "[FromUnitId] <> [ToUnitId]");
                table.CheckConstraint("CK_UnitConversions_Factor", "[Factor] > 0");
                table.CheckConstraint("CK_UnitConversions_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_UnitConversions_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_UnitConversions_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_UnitConversions_Items_ItemId",
                    column: x => x.ItemId,
                    principalSchema: "inventory",
                    principalTable: "Items",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_UnitConversions_UnitsOfMeasure_FromUnitId",
                    column: x => x.FromUnitId,
                    principalSchema: "inventory",
                    principalTable: "UnitsOfMeasure",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_UnitConversions_UnitsOfMeasure_ToUnitId",
                    column: x => x.ToUnitId,
                    principalSchema: "inventory",
                    principalTable: "UnitsOfMeasure",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "StockAdjustmentLines",
            schema: "inventory",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AdjustmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SystemQuantity = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                CountedQuantity = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                DifferenceQuantity = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                UnitCost = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StockAdjustmentLines", x => x.Id);
                table.CheckConstraint("CK_StockAdjustmentLines_CountedQuantity", "[CountedQuantity] >= 0");
                table.CheckConstraint("CK_StockAdjustmentLines_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_StockAdjustmentLines_UnitCost", "[UnitCost] >= 0");
                table.ForeignKey(
                    name: "FK_StockAdjustmentLines_Items_ItemId",
                    column: x => x.ItemId,
                    principalSchema: "inventory",
                    principalTable: "Items",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_StockAdjustmentLines_StockAdjustments_AdjustmentId",
                    column: x => x.AdjustmentId,
                    principalSchema: "inventory",
                    principalTable: "StockAdjustments",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_StockAdjustmentLines_UnitsOfMeasure_UnitId",
                    column: x => x.UnitId,
                    principalSchema: "inventory",
                    principalTable: "UnitsOfMeasure",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "StockTransferLines",
            schema: "inventory",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Quantity = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                BaseQuantity = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                UnitCost = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                TotalCost = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StockTransferLines", x => x.Id);
                table.CheckConstraint("CK_StockTransferLines_BaseQuantity", "[BaseQuantity] > 0");
                table.CheckConstraint("CK_StockTransferLines_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_StockTransferLines_Quantity", "[Quantity] > 0");
                table.CheckConstraint("CK_StockTransferLines_TotalCost", "[TotalCost] >= 0");
                table.CheckConstraint("CK_StockTransferLines_UnitCost", "[UnitCost] >= 0");
                table.ForeignKey(
                    name: "FK_StockTransferLines_Items_ItemId",
                    column: x => x.ItemId,
                    principalSchema: "inventory",
                    principalTable: "Items",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_StockTransferLines_StockTransfers_TransferId",
                    column: x => x.TransferId,
                    principalSchema: "inventory",
                    principalTable: "StockTransfers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_StockTransferLines_UnitsOfMeasure_UnitId",
                    column: x => x.UnitId,
                    principalSchema: "inventory",
                    principalTable: "UnitsOfMeasure",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Documents",
            schema: "inventory",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Kind = table.Column<int>(type: "int", nullable: false),
                State = table.Column<int>(type: "int", nullable: false),
                WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TargetWarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                DateUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                CostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                InventoryAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                OffsetAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Number = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                JournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ReversalJournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PostedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                PostedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                PostKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2"),
                CreationFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                CreationKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2"),
                PostVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                PostReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                ReverseKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2"),
                ReverseVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                ReverseReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                ReversedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ReversedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Documents", x => x.Id);
                table.CheckConstraint("CK_InventoryDocuments_Center", "[Kind]<>3 OR [CostCenterId] IS NOT NULL");
                table.CheckConstraint("CK_InventoryDocuments_Kind", "[Kind] IN (1,2,3)");
                table.CheckConstraint("CK_InventoryDocuments_Mappings", "([Kind]=1 AND [InventoryAccountId] IS NULL AND [OffsetAccountId] IS NULL) OR ([Kind]<>1 AND [InventoryAccountId] IS NOT NULL AND [OffsetAccountId] IS NOT NULL AND [InventoryAccountId]<>[OffsetAccountId])");
                table.CheckConstraint("CK_InventoryDocuments_Posted", "[State]=0 OR ([Number] IS NOT NULL AND [PostKey] IS NOT NULL AND [PostedBy] IS NOT NULL AND [PostedAtUtc] IS NOT NULL)");
                table.CheckConstraint("CK_InventoryDocuments_Reason", "LEN(LTRIM(RTRIM([Reason])))>0");
                table.CheckConstraint("CK_InventoryDocuments_Reversed", "[State]<>2 OR ([ReverseKey] IS NOT NULL AND [ReversedBy] IS NOT NULL AND [ReversedAtUtc] IS NOT NULL)");
                table.CheckConstraint("CK_InventoryDocuments_State", "[State] IN (0,1,2)");
                table.CheckConstraint("CK_InventoryDocuments_Target", "([Kind]=1 AND [TargetWarehouseId] IS NOT NULL AND [TargetWarehouseId]<>[WarehouseId]) OR ([Kind]<>1 AND [TargetWarehouseId] IS NULL)");
                table.CheckConstraint("CK_InventoryDocuments_Utc", "DATEPART(TZOFFSET,[DateUtc])=0");
                table.ForeignKey(
                    name: "FK_Documents_Accounts_InventoryAccountId",
                    column: x => x.InventoryAccountId,
                    principalSchema: "ledger",
                    principalTable: "Accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Documents_Accounts_OffsetAccountId",
                    column: x => x.OffsetAccountId,
                    principalSchema: "ledger",
                    principalTable: "Accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Documents_ActorReferences_CompanyId_CreatedBy",
                    columns: x => new { x.CompanyId, x.CreatedBy },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Documents_ActorReferences_CompanyId_PostedBy",
                    columns: x => new { x.CompanyId, x.PostedBy },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Documents_ActorReferences_CompanyId_ReversedBy",
                    columns: x => new { x.CompanyId, x.ReversedBy },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Documents_Branches_CompanyId_BranchId",
                    columns: x => new { x.CompanyId, x.BranchId },
                    principalSchema: "system",
                    principalTable: "Branches",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Documents_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Documents_CostCenters_CostCenterId",
                    column: x => x.CostCenterId,
                    principalSchema: "ledger",
                    principalTable: "CostCenters",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Documents_JournalEntries_JournalId",
                    column: x => x.JournalId,
                    principalSchema: "ledger",
                    principalTable: "JournalEntries",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Documents_JournalEntries_ReversalJournalId",
                    column: x => x.ReversalJournalId,
                    principalSchema: "ledger",
                    principalTable: "JournalEntries",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Documents_Warehouses_TargetWarehouseId",
                    column: x => x.TargetWarehouseId,
                    principalSchema: "inventory",
                    principalTable: "Warehouses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Documents_Warehouses_WarehouseId",
                    column: x => x.WarehouseId,
                    principalSchema: "inventory",
                    principalTable: "Warehouses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Invoices",
            schema: "purchases",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                VarianceAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    name: "FK_Invoices_Accounts_VarianceAccountId",
                    column: x => x.VarianceAccountId,
                    principalSchema: "ledger",
                    principalTable: "Accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Invoices_ActorReferences_CompanyId_CreatedByUserId",
                    columns: x => new { x.CompanyId, x.CreatedByUserId },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Invoices_ActorReferences_CompanyId_PostedByUserId",
                    columns: x => new { x.CompanyId, x.PostedByUserId },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Invoices_Branches_CompanyId_BranchId",
                    columns: x => new { x.CompanyId, x.BranchId },
                    principalSchema: "system",
                    principalTable: "Branches",
                    principalColumns: KeyColumns001,
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
                ReturnOfInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                table.CheckConstraint("CK_SalesInvoices_ReturnKind", "[ReturnOfInvoiceId] IS NULL OR ([ReversesInvoiceId] IS NULL AND [ReversedByInvoiceId] IS NULL AND [Status] = 2 AND [JournalEntryId] IS NOT NULL AND [ReturnOfInvoiceId] <> [Id])");
                table.CheckConstraint("CK_SalesInvoices_Status", "[Status] IN (0, 1, 2, 3, 4)");
                table.CheckConstraint("CK_SalesInvoices_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                table.CheckConstraint("CK_SalesInvoices_WarehouseId", "[WarehouseId] <> '00000000-0000-0000-0000-000000000000'");
                table.ForeignKey(
                    name: "FK_Invoices_ActorReferences_CompanyId_ApprovedByUserId",
                    columns: x => new { x.CompanyId, x.ApprovedByUserId },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Invoices_ActorReferences_CompanyId_CreatedByUserId",
                    columns: x => new { x.CompanyId, x.CreatedByUserId },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Invoices_ActorReferences_CompanyId_PostedByUserId",
                    columns: x => new { x.CompanyId, x.PostedByUserId },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Invoices_Branches_CompanyId_BranchId",
                    columns: x => new { x.CompanyId, x.BranchId },
                    principalSchema: "system",
                    principalTable: "Branches",
                    principalColumns: KeyColumns001,
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
                    name: "FK_Invoices_Invoices_ReturnOfInvoiceId",
                    column: x => x.ReturnOfInvoiceId,
                    principalSchema: "sales",
                    principalTable: "Invoices",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Invoices_Invoices_ReversedByInvoiceId",
                    column: x => x.ReversedByInvoiceId,
                    principalSchema: "sales",
                    principalTable: "Invoices",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Invoices_Invoices_ReversesInvoiceId",
                    column: x => x.ReversesInvoiceId,
                    principalSchema: "sales",
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
            name: "JournalAudit",
            schema: "ledger",
            columns: table => new
            {
                EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                JournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Revision = table.Column<long>(type: "bigint", nullable: false),
                Action = table.Column<byte>(type: "tinyint", nullable: false),
                PreviousState = table.Column<byte>(type: "tinyint", nullable: true),
                NewState = table.Column<byte>(type: "tinyint", nullable: false),
                ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                RelatedEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_JournalAudit", x => x.EventId);
                table.CheckConstraint("CK_JournalAudit_Action", "[Action] IN (1, 2, 3, 4, 5, 6, 7)");
                table.CheckConstraint("CK_JournalAudit_ActorUserId", "[ActorUserId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_JournalAudit_BranchId", "[BranchId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_JournalAudit_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_JournalAudit_CorrelationId", "[CorrelationId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_JournalAudit_CreatedAtUtc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                table.CheckConstraint("CK_JournalAudit_EventId", "[EventId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_JournalAudit_JournalId", "[JournalId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_JournalAudit_NewState", "[NewState] IN (1, 2, 3, 4, 5)");
                table.CheckConstraint("CK_JournalAudit_OccurredAtUtc", "DATEPART(TZOFFSET, [OccurredAtUtc]) = 0");
                table.CheckConstraint("CK_JournalAudit_PreviousState", "[PreviousState] IS NULL OR [PreviousState] IN (1, 2, 3, 4, 5)");
                table.CheckConstraint("CK_JournalAudit_Reason", "DATALENGTH([Reason]) > 0 AND DATALENGTH([Reason]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Reason]))");
                table.CheckConstraint("CK_JournalAudit_Revision", "[Revision] > 0");
                table.ForeignKey(
                    name: "FK_JournalAudit_ActorReferences_CompanyId_ActorUserId",
                    columns: x => new { x.CompanyId, x.ActorUserId },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_JournalAudit_JournalEntries",
                    column: x => x.JournalId,
                    principalSchema: "ledger",
                    principalTable: "JournalEntries",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "JournalLines",
            schema: "ledger",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                JournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                TransactionCurrencyCode = table.Column<string>(type: "char(3)", nullable: false, collation: "Latin1_General_100_BIN2"),
                TransactionDebit = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                TransactionCredit = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                BaseCurrencyCode = table.Column<string>(type: "char(3)", nullable: false, collation: "Latin1_General_100_BIN2"),
                BaseDebit = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                BaseCredit = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                RateSourceCurrencyCode = table.Column<string>(type: "char(3)", nullable: false, collation: "Latin1_General_100_BIN2"),
                RateTargetCurrencyCode = table.Column<string>(type: "char(3)", nullable: false, collation: "Latin1_General_100_BIN2"),
                RateValue = table.Column<decimal>(type: "decimal(19,8)", nullable: false),
                Rounding = table.Column<byte>(type: "tinyint", nullable: false),
                Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                ReversesLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_JournalLines", x => x.Id);
                table.CheckConstraint("CK_JournalLines_AccountId", "[AccountId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_JournalLines_BaseAmounts", "[BaseDebit] >= 0 AND [BaseCredit] >= 0 AND ([BaseDebit] > 0 OR [BaseCredit] > 0) AND ([BaseDebit] = 0 OR [BaseCredit] = 0)");
                table.CheckConstraint("CK_JournalLines_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_JournalLines_Description", "DATALENGTH([Description]) > 0 AND DATALENGTH([Description]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Description]))");
                table.CheckConstraint("CK_JournalLines_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_JournalLines_JournalId", "[JournalId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_JournalLines_NotSelfReversal", "[ReversesLineId] IS NULL OR [ReversesLineId] <> [Id]");
                table.CheckConstraint("CK_JournalLines_RateValue", "[RateValue] > 0");
                table.CheckConstraint("CK_JournalLines_Rounding", "[Rounding] IN (0, 1, 2, 3, 4)");
                table.CheckConstraint("CK_JournalLines_TransactionAmounts", "[TransactionDebit] >= 0 AND [TransactionCredit] >= 0 AND ([TransactionDebit] > 0 OR [TransactionCredit] > 0) AND ([TransactionDebit] = 0 OR [TransactionCredit] = 0)");
                table.CheckConstraint("CK_JournalLines_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_JournalLines_Accounts",
                    column: x => x.AccountId,
                    principalSchema: "ledger",
                    principalTable: "Accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_JournalLines_BaseCurrency",
                    column: x => x.BaseCurrencyCode,
                    principalSchema: "system",
                    principalTable: "Currencies",
                    principalColumn: "Code",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_JournalLines_CostCenters",
                    column: x => x.CostCenterId,
                    principalSchema: "ledger",
                    principalTable: "CostCenters",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_JournalLines_JournalEntries",
                    column: x => x.JournalId,
                    principalSchema: "ledger",
                    principalTable: "JournalEntries",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_JournalLines_TransactionCurrency",
                    column: x => x.TransactionCurrencyCode,
                    principalSchema: "system",
                    principalTable: "Currencies",
                    principalColumn: "Code",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Vouchers",
            schema: "treasury",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Kind = table.Column<byte>(type: "tinyint", nullable: false),
                PatternRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                SettlementMethod = table.Column<byte>(type: "tinyint", nullable: false),
                VoucherNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                VoucherDateUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                CurrencyCode = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                ExchangeRate = table.Column<decimal>(type: "decimal(19,8)", precision: 19, scale: 8, nullable: false),
                Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                Status = table.Column<byte>(type: "tinyint", nullable: false),
                ApprovedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                ApprovalReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                PartyControlAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                PostingKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2"),
                JournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ReversalJournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ReversalReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PostedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                PostedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                ReversedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ReversedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Vouchers", x => x.Id);
                table.CheckConstraint("CK_Vouchers_Amount", "[Amount] > 0");
                table.CheckConstraint("CK_Vouchers_ExchangeRate", "[ExchangeRate] > 0");
                table.CheckConstraint("CK_Vouchers_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_Vouchers_Kind", "[Kind] IN (1, 2)");
                table.CheckConstraint("CK_Vouchers_Posted", "[Status] IN (0,1) OR ([JournalEntryId] IS NOT NULL AND [VoucherNumber] IS NOT NULL AND [PostingKey] IS NOT NULL AND [PostedAtUtc] IS NOT NULL)");
                table.CheckConstraint("CK_Vouchers_ReceiptParty", "[Kind] <> 1 OR [PartyId] IS NOT NULL");
                table.CheckConstraint("CK_Vouchers_Reversed", "([Status] = 3 AND [ReversalJournalEntryId] IS NOT NULL AND [ReversedAtUtc] IS NOT NULL AND [ReversalReason] IS NOT NULL) OR ([Status] <> 3 AND [ReversalJournalEntryId] IS NULL AND [ReversedAtUtc] IS NULL AND [ReversalReason] IS NULL)");
                table.CheckConstraint("CK_Vouchers_SettlementMethod", "[SettlementMethod] IN (1, 2)");
                table.CheckConstraint("CK_Vouchers_Status", "[Status] IN (0, 1, 2, 3)");
                table.CheckConstraint("CK_Vouchers_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                table.CheckConstraint("CK_Vouchers_VoucherDateUtc", "DATEPART(TZOFFSET, [VoucherDateUtc]) = 0");
                table.ForeignKey(
                    name: "FK_Vouchers_Accounts_PartyControlAccountId",
                    column: x => x.PartyControlAccountId,
                    principalSchema: "ledger",
                    principalTable: "Accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Vouchers_ActorReferences_CompanyId_ApprovedBy",
                    columns: x => new { x.CompanyId, x.ApprovedBy },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Vouchers_ActorReferences_CompanyId_CreatedByUserId",
                    columns: x => new { x.CompanyId, x.CreatedByUserId },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Vouchers_ActorReferences_CompanyId_PostedByUserId",
                    columns: x => new { x.CompanyId, x.PostedByUserId },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Vouchers_Branches_CompanyId_BranchId",
                    columns: x => new { x.CompanyId, x.BranchId },
                    principalSchema: "system",
                    principalTable: "Branches",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Vouchers_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Vouchers_JournalEntries_JournalEntryId",
                    column: x => x.JournalEntryId,
                    principalSchema: "ledger",
                    principalTable: "JournalEntries",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Vouchers_JournalEntries_ReversalJournalEntryId",
                    column: x => x.ReversalJournalEntryId,
                    principalSchema: "ledger",
                    principalTable: "JournalEntries",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Vouchers_Parties_PartyId",
                    column: x => x.PartyId,
                    principalSchema: "parties",
                    principalTable: "Parties",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Vouchers_PatternRevisions_PatternRevisionId",
                    column: x => x.PatternRevisionId,
                    principalSchema: "patterns",
                    principalTable: "PatternRevisions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "DocumentLines",
            schema: "inventory",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OpeningUnitCost = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Position = table.Column<int>(type: "int", nullable: false),
                Quantity = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                BaseQuantity = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                SnapshotQuantity = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                SnapshotSequence = table.Column<long>(type: "bigint", nullable: false),
                Cost = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DocumentLines", x => x.Id);
                table.CheckConstraint("CK_InventoryDocumentLines_OpeningCost", "[OpeningUnitCost] IS NULL OR [OpeningUnitCost]>=0");
                table.CheckConstraint("CK_InventoryDocumentLines_Quantity", "[Quantity]>=0 AND [BaseQuantity]>=0 AND [Cost]>=0");
                table.ForeignKey(
                    name: "FK_DocumentLines_Documents_DocumentId",
                    column: x => x.DocumentId,
                    principalSchema: "inventory",
                    principalTable: "Documents",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_DocumentLines_Items_ItemId",
                    column: x => x.ItemId,
                    principalSchema: "inventory",
                    principalTable: "Items",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_DocumentLines_UnitsOfMeasure_UnitId",
                    column: x => x.UnitId,
                    principalSchema: "inventory",
                    principalTable: "UnitsOfMeasure",
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

        migrationBuilder.CreateTable(
            name: "CreditAudit",
            schema: "sales",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                IdempotencyKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PolicyKind = table.Column<int>(type: "int", nullable: false),
                WasCreditBlocked = table.Column<bool>(type: "bit", nullable: false),
                IsOverride = table.Column<bool>(type: "bit", nullable: false),
                CreditLimitBase = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                ExposureBeforeBase = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                InvoiceBaseAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                ExposureAfterBase = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CreditAudit", x => x.Id);
                table.CheckConstraint("CK_SalesCreditAudit_Amounts", "[CreditLimitBase] >= 0 AND [InvoiceBaseAmount] >= 0 AND [ExposureAfterBase] = [ExposureBeforeBase] + [InvoiceBaseAmount]");
                table.CheckConstraint("CK_SalesCreditAudit_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_SalesCreditAudit_Policy", "[PolicyKind] IN (1,2,3) AND ([WasCreditBlocked] = 0 OR [IsOverride] = 1)");
                table.CheckConstraint("CK_SalesCreditAudit_Reason", "LEN(LTRIM(RTRIM([Reason]))) > 0");
                table.CheckConstraint("CK_SalesCreditAudit_Utc", "DATEPART(TZOFFSET,[RecordedAtUtc]) = 0");
                table.ForeignKey(
                    name: "FK_CreditAudit_ActorReferences_CompanyId_ActorUserId",
                    columns: x => new { x.CompanyId, x.ActorUserId },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CreditAudit_Branches_CompanyId_BranchId",
                    columns: x => new { x.CompanyId, x.BranchId },
                    principalSchema: "system",
                    principalTable: "Branches",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CreditAudit_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CreditAudit_Invoices_InvoiceId",
                    column: x => x.InvoiceId,
                    principalSchema: "sales",
                    principalTable: "Invoices",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CreditAudit_Parties_PartyId",
                    column: x => x.PartyId,
                    principalSchema: "parties",
                    principalTable: "Parties",
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
                ConcreteGrade = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                SourceLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_InvoiceLines", x => x.Id);
                table.CheckConstraint("CK_SalesInvoiceLines_DiscountAmount", "[DiscountAmount] >= 0");
                table.CheckConstraint("CK_SalesInvoiceLines_DiscountPercentage", "[DiscountPercentage] >= 0 AND [DiscountPercentage] <= 100");
                table.CheckConstraint("CK_SalesInvoiceLines_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_SalesInvoiceLines_PumpCost", "[PumpCost] >= 0");
                table.CheckConstraint("CK_SalesInvoiceLines_Quantity", "[Quantity] > 0");
                table.CheckConstraint("CK_SalesInvoiceLines_SourceLineId", "[SourceLineId] IS NULL OR [SourceLineId] <> [Id]");
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
                    name: "FK_InvoiceLines_InvoiceLines_SourceLineId",
                    column: x => x.SourceLineId,
                    principalSchema: "sales",
                    principalTable: "InvoiceLines",
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

        migrationBuilder.CreateTable(
            name: "PostingRequests",
            schema: "sales",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Operation = table.Column<byte>(type: "tinyint", nullable: false),
                IdempotencyKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ExpectedInvoiceVersion = table.Column<byte[]>(type: "binary(8)", nullable: false),
                Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                FingerprintHex = table.Column<string>(type: "char(64)", nullable: false),
                RegisteredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                OutcomeJournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PostingRequests", x => new { x.CompanyId, x.BranchId, x.Operation, x.IdempotencyKey });
                table.CheckConstraint("CK_SalesPostingRequests_Completion", "([CompletedAtUtc] IS NULL AND [OutcomeJournalId] IS NULL) OR ([CompletedAtUtc] IS NOT NULL AND [OutcomeJournalId] IS NOT NULL)");
                table.CheckConstraint("CK_SalesPostingRequests_Key", "[IdempotencyKey]<>'00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_SalesPostingRequests_Operation", "[Operation] IN (1,2)");
                table.CheckConstraint("CK_SalesPostingRequests_Version", "DATALENGTH([ExpectedInvoiceVersion])=8");
                table.ForeignKey(
                    name: "FK_PostingRequests_ActorReferences_CompanyId_ActorUserId",
                    columns: x => new { x.CompanyId, x.ActorUserId },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PostingRequests_Branches_CompanyId_BranchId",
                    columns: x => new { x.CompanyId, x.BranchId },
                    principalSchema: "system",
                    principalTable: "Branches",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PostingRequests_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PostingRequests_Invoices_InvoiceId",
                    column: x => x.InvoiceId,
                    principalSchema: "sales",
                    principalTable: "Invoices",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PostingRequests_JournalEntries_OutcomeJournalId",
                    column: x => x.OutcomeJournalId,
                    principalSchema: "ledger",
                    principalTable: "JournalEntries",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "AllocationPlans",
            schema: "treasury",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                VoucherId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SalesInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                PurchaseInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                InvoiceAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                SettlementAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                CrossRate = table.Column<decimal>(type: "decimal(19,8)", precision: 19, scale: 8, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AllocationPlans", x => x.Id);
                table.CheckConstraint("CK_AllocationPlans_Amounts", "[InvoiceAmount]>0 AND [SettlementAmount]>0 AND [CrossRate]>0");
                table.CheckConstraint("CK_AllocationPlans_Target", "([SalesInvoiceId] IS NULL AND [PurchaseInvoiceId] IS NOT NULL) OR ([SalesInvoiceId] IS NOT NULL AND [PurchaseInvoiceId] IS NULL)");
                table.ForeignKey(
                    name: "FK_AllocationPlans_Invoices_PurchaseInvoiceId",
                    column: x => x.PurchaseInvoiceId,
                    principalSchema: "purchases",
                    principalTable: "Invoices",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AllocationPlans_Invoices_SalesInvoiceId",
                    column: x => x.SalesInvoiceId,
                    principalSchema: "sales",
                    principalTable: "Invoices",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AllocationPlans_Vouchers_VoucherId",
                    column: x => x.VoucherId,
                    principalSchema: "treasury",
                    principalTable: "Vouchers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "CommercialPapers",
            schema: "treasury",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PaperKind = table.Column<byte>(type: "tinyint", nullable: false),
                PaperNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                BankName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                BranchName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                DrawerName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                PayeeName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                IssueDate = table.Column<DateOnly>(type: "date", nullable: false),
                DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                CurrencyCode = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                ExchangeRate = table.Column<decimal>(type: "decimal(19,8)", precision: 19, scale: 8, nullable: false),
                BaseAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                Status = table.Column<byte>(type: "tinyint", nullable: false),
                StatusDate = table.Column<DateOnly>(type: "date", nullable: false),
                PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                PortfolioAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                IntermediaryAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                BankAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                EndorsedPartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                VoucherId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                LastJournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CommercialPapers", x => x.Id);
                table.CheckConstraint("CK_CommercialPapers_Amount", "[Amount] > 0");
                table.CheckConstraint("CK_CommercialPapers_ExchangeRate", "[ExchangeRate] > 0");
                table.CheckConstraint("CK_CommercialPapers_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_CommercialPapers_Kind", "[PaperKind] IN (1, 2, 3)");
                table.CheckConstraint("CK_CommercialPapers_Status", "[Status] IN (1, 2, 3, 4, 5, 6)");
                table.ForeignKey(
                    name: "FK_CommercialPapers_Accounts_BankAccountId",
                    column: x => x.BankAccountId,
                    principalSchema: "ledger",
                    principalTable: "Accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CommercialPapers_Accounts_IntermediaryAccountId",
                    column: x => x.IntermediaryAccountId,
                    principalSchema: "ledger",
                    principalTable: "Accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CommercialPapers_Accounts_PortfolioAccountId",
                    column: x => x.PortfolioAccountId,
                    principalSchema: "ledger",
                    principalTable: "Accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CommercialPapers_ActorReferences_CompanyId_CreatedByUserId",
                    columns: x => new { x.CompanyId, x.CreatedByUserId },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CommercialPapers_JournalEntries_LastJournalEntryId",
                    column: x => x.LastJournalEntryId,
                    principalSchema: "ledger",
                    principalTable: "JournalEntries",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CommercialPapers_Parties_EndorsedPartyId",
                    column: x => x.EndorsedPartyId,
                    principalSchema: "parties",
                    principalTable: "Parties",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CommercialPapers_Parties_PartyId",
                    column: x => x.PartyId,
                    principalSchema: "parties",
                    principalTable: "Parties",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CommercialPapers_Vouchers_VoucherId",
                    column: x => x.VoucherId,
                    principalSchema: "treasury",
                    principalTable: "Vouchers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "SettlementBatches",
            schema: "treasury",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                VoucherId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                IsInitial = table.Column<bool>(type: "bit", nullable: false),
                State = table.Column<int>(type: "int", nullable: false),
                DateUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                RequestKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                JournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ReversalJournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ReversedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ReversedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                ReversalReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SettlementBatches", x => x.Id);
                table.CheckConstraint("CK_SettlementBatches_Reason", "LEN(LTRIM(RTRIM([Reason])))>0");
                table.CheckConstraint("CK_SettlementBatches_Reversal", "[State]=1 OR ([ReversedAtUtc] IS NOT NULL AND [ReversedBy] IS NOT NULL AND [ReversalReason] IS NOT NULL)");
                table.CheckConstraint("CK_SettlementBatches_State", "[State] IN (1,2)");
                table.CheckConstraint("CK_SettlementBatches_Utc", "DATEPART(TZOFFSET,[DateUtc])=0 AND DATEPART(TZOFFSET,[RecordedAtUtc])=0");
                table.ForeignKey(
                    name: "FK_SettlementBatches_ActorReferences_CompanyId_ActorId",
                    columns: x => new { x.CompanyId, x.ActorId },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_SettlementBatches_ActorReferences_CompanyId_ReversedBy",
                    columns: x => new { x.CompanyId, x.ReversedBy },
                    principalSchema: "audit",
                    principalTable: "ActorReferences",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_SettlementBatches_Branches_CompanyId_BranchId",
                    columns: x => new { x.CompanyId, x.BranchId },
                    principalSchema: "system",
                    principalTable: "Branches",
                    principalColumns: KeyColumns001,
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_SettlementBatches_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "system",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_SettlementBatches_JournalEntries_JournalId",
                    column: x => x.JournalId,
                    principalSchema: "ledger",
                    principalTable: "JournalEntries",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_SettlementBatches_JournalEntries_ReversalJournalId",
                    column: x => x.ReversalJournalId,
                    principalSchema: "ledger",
                    principalTable: "JournalEntries",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_SettlementBatches_Vouchers_VoucherId",
                    column: x => x.VoucherId,
                    principalSchema: "treasury",
                    principalTable: "Vouchers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "VoucherAllocations",
            schema: "treasury",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                VoucherId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SalesInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_VoucherAllocations", x => x.Id);
                table.CheckConstraint("CK_VoucherAllocations_Amount", "[Amount] > 0");
                table.CheckConstraint("CK_VoucherAllocations_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.ForeignKey(
                    name: "FK_VoucherAllocations_Invoices_SalesInvoiceId",
                    column: x => x.SalesInvoiceId,
                    principalSchema: "sales",
                    principalTable: "Invoices",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_VoucherAllocations_Vouchers_VoucherId",
                    column: x => x.VoucherId,
                    principalSchema: "treasury",
                    principalTable: "Vouchers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "VoucherLines",
            schema: "treasury",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                VoucherId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                CostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                DisplayOrder = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_VoucherLines", x => x.Id);
                table.CheckConstraint("CK_VoucherLines_Amount", "[Amount] > 0");
                table.CheckConstraint("CK_VoucherLines_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_VoucherLines_Target", "([PartyId] IS NULL AND [AccountId] IS NOT NULL) OR ([PartyId] IS NOT NULL AND [AccountId] IS NULL)");
                table.ForeignKey(
                    name: "FK_VoucherLines_Accounts_AccountId",
                    column: x => x.AccountId,
                    principalSchema: "ledger",
                    principalTable: "Accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_VoucherLines_CostCenters_CostCenterId",
                    column: x => x.CostCenterId,
                    principalSchema: "ledger",
                    principalTable: "CostCenters",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_VoucherLines_Parties_PartyId",
                    column: x => x.PartyId,
                    principalSchema: "parties",
                    principalTable: "Parties",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_VoucherLines_Vouchers_VoucherId",
                    column: x => x.VoucherId,
                    principalSchema: "treasury",
                    principalTable: "Vouchers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "SettlementApplications",
            schema: "treasury",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                VoucherId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SalesInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                PurchaseInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                InvoiceCurrency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                SettlementCurrency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                InvoiceAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                SettlementAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                CrossRate = table.Column<decimal>(type: "decimal(19,8)", precision: 19, scale: 8, nullable: false),
                InvoiceRate = table.Column<decimal>(type: "decimal(19,8)", precision: 19, scale: 8, nullable: false),
                SettlementRate = table.Column<decimal>(type: "decimal(19,8)", precision: 19, scale: 8, nullable: false),
                BookedBaseAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                SettlementBaseAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SettlementApplications", x => x.Id);
                table.CheckConstraint("CK_SettlementApplications_Amounts", "[InvoiceAmount]>0 AND [SettlementAmount]>0 AND [BookedBaseAmount]>=0 AND [SettlementBaseAmount]>=0 AND [InvoiceRate]>0 AND [SettlementRate]>0 AND [CrossRate]>0");
                table.CheckConstraint("CK_SettlementApplications_Target", "([SalesInvoiceId] IS NULL AND [PurchaseInvoiceId] IS NOT NULL) OR ([SalesInvoiceId] IS NOT NULL AND [PurchaseInvoiceId] IS NULL)");
                table.ForeignKey(
                    name: "FK_SettlementApplications_Accounts_AccountId",
                    column: x => x.AccountId,
                    principalSchema: "ledger",
                    principalTable: "Accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_SettlementApplications_Invoices_PurchaseInvoiceId",
                    column: x => x.PurchaseInvoiceId,
                    principalSchema: "purchases",
                    principalTable: "Invoices",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_SettlementApplications_Invoices_SalesInvoiceId",
                    column: x => x.SalesInvoiceId,
                    principalSchema: "sales",
                    principalTable: "Invoices",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_SettlementApplications_SettlementBatches_BatchId",
                    column: x => x.BatchId,
                    principalSchema: "treasury",
                    principalTable: "SettlementBatches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_SettlementApplications_Vouchers_VoucherId",
                    column: x => x.VoucherId,
                    principalSchema: "treasury",
                    principalTable: "Vouchers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AccountAudit_AccountHistory",
            schema: "ledger",
            table: "AccountAudit",
            columns: KeyColumns003);

        migrationBuilder.CreateIndex(
            name: "IX_AccountAudit_CompanyAccount",
            schema: "ledger",
            table: "AccountAudit",
            columns: KeyColumns004);

        migrationBuilder.CreateIndex(
            name: "IX_AccountAudit_CompanyId_ActorUserId",
            schema: "ledger",
            table: "AccountAudit",
            columns: KeyColumns005);

        migrationBuilder.CreateIndex(
            name: "IX_AccountAudit_CompanyId_WorkingBranchId",
            schema: "ledger",
            table: "AccountAudit",
            columns: KeyColumns006);

        migrationBuilder.CreateIndex(
            name: "IX_AccountAudit_CompanyTime",
            schema: "ledger",
            table: "AccountAudit",
            columns: KeyColumns007);

        migrationBuilder.CreateIndex(
            name: "IX_AccountingPeriods_CompanyDateRange",
            schema: "ledger",
            table: "AccountingPeriods",
            columns: KeyColumns008);

        migrationBuilder.CreateIndex(
            name: "UQ_AccountingPeriods_YearCode",
            schema: "ledger",
            table: "AccountingPeriods",
            columns: KeyColumns009,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Accounts_CompanyActivePosting",
            schema: "ledger",
            table: "Accounts",
            columns: KeyColumns010);

        migrationBuilder.CreateIndex(
            name: "IX_Accounts_FixedCurrencyCode",
            schema: "ledger",
            table: "Accounts",
            column: "FixedCurrencyCode");

        migrationBuilder.CreateIndex(
            name: "IX_Accounts_ParentId",
            schema: "ledger",
            table: "Accounts",
            column: "ParentId");

        migrationBuilder.CreateIndex(
            name: "UQ_Accounts_CompanyCode",
            schema: "ledger",
            table: "Accounts",
            columns: KeyColumns011,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_AllocationPlans_PurchaseInvoiceId",
            schema: "treasury",
            table: "AllocationPlans",
            column: "PurchaseInvoiceId");

        migrationBuilder.CreateIndex(
            name: "IX_AllocationPlans_SalesInvoiceId",
            schema: "treasury",
            table: "AllocationPlans",
            column: "SalesInvoiceId");

        migrationBuilder.CreateIndex(
            name: "IX_AllocationPlans_VoucherId_PurchaseInvoiceId",
            schema: "treasury",
            table: "AllocationPlans",
            columns: KeyColumns012,
            unique: true,
            filter: "[PurchaseInvoiceId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_AllocationPlans_VoucherId_SalesInvoiceId",
            schema: "treasury",
            table: "AllocationPlans",
            columns: KeyColumns013,
            unique: true,
            filter: "[SalesInvoiceId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Branches_CompanyId_Code",
            schema: "system",
            table: "Branches",
            columns: KeyColumns011,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_CommercialPapers_BankAccountId",
            schema: "treasury",
            table: "CommercialPapers",
            column: "BankAccountId");

        migrationBuilder.CreateIndex(
            name: "IX_CommercialPapers_CompanyId_BranchId_PaperNumber",
            schema: "treasury",
            table: "CommercialPapers",
            columns: KeyColumns014);

        migrationBuilder.CreateIndex(
            name: "IX_CommercialPapers_CompanyId_BranchId_Status",
            schema: "treasury",
            table: "CommercialPapers",
            columns: KeyColumns015);

        migrationBuilder.CreateIndex(
            name: "IX_CommercialPapers_CompanyId_CreatedByUserId",
            schema: "treasury",
            table: "CommercialPapers",
            columns: KeyColumns016);

        migrationBuilder.CreateIndex(
            name: "IX_CommercialPapers_CompanyId_DueDate_Status",
            schema: "treasury",
            table: "CommercialPapers",
            columns: KeyColumns017);

        migrationBuilder.CreateIndex(
            name: "IX_CommercialPapers_EndorsedPartyId",
            schema: "treasury",
            table: "CommercialPapers",
            column: "EndorsedPartyId");

        migrationBuilder.CreateIndex(
            name: "IX_CommercialPapers_IntermediaryAccountId",
            schema: "treasury",
            table: "CommercialPapers",
            column: "IntermediaryAccountId");

        migrationBuilder.CreateIndex(
            name: "IX_CommercialPapers_LastJournalEntryId",
            schema: "treasury",
            table: "CommercialPapers",
            column: "LastJournalEntryId");

        migrationBuilder.CreateIndex(
            name: "IX_CommercialPapers_PartyId",
            schema: "treasury",
            table: "CommercialPapers",
            column: "PartyId");

        migrationBuilder.CreateIndex(
            name: "IX_CommercialPapers_PortfolioAccountId",
            schema: "treasury",
            table: "CommercialPapers",
            column: "PortfolioAccountId");

        migrationBuilder.CreateIndex(
            name: "IX_CommercialPapers_VoucherId",
            schema: "treasury",
            table: "CommercialPapers",
            column: "VoucherId");

        migrationBuilder.CreateIndex(
            name: "IX_Companies_BaseCurrencyCode",
            schema: "system",
            table: "Companies",
            column: "BaseCurrencyCode");

        migrationBuilder.CreateIndex(
            name: "IX_Companies_Code",
            schema: "system",
            table: "Companies",
            column: "Code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_CompanyAdditionalCurrencies_CurrencyCode",
            schema: "system",
            table: "CompanyAdditionalCurrencies",
            column: "CurrencyCode");

        migrationBuilder.CreateIndex(
            name: "IX_CostCenterAudit_CompanyId_ActorUserId",
            schema: "ledger",
            table: "CostCenterAudit",
            columns: KeyColumns005);

        migrationBuilder.CreateIndex(
            name: "IX_CostCenterAudit_CompanyId_WorkingBranchId",
            schema: "ledger",
            table: "CostCenterAudit",
            columns: KeyColumns006);

        migrationBuilder.CreateIndex(
            name: "IX_CostCenterAudit_History",
            schema: "ledger",
            table: "CostCenterAudit",
            columns: KeyColumns018);

        migrationBuilder.CreateIndex(
            name: "UQ_CostCenters_CompanyCode",
            schema: "ledger",
            table: "CostCenters",
            columns: KeyColumns011,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_CreditAudit_CompanyId_ActorUserId",
            schema: "sales",
            table: "CreditAudit",
            columns: KeyColumns005);

        migrationBuilder.CreateIndex(
            name: "IX_CreditAudit_CompanyId_BranchId_InvoiceId",
            schema: "sales",
            table: "CreditAudit",
            columns: KeyColumns019,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_CreditAudit_InvoiceId",
            schema: "sales",
            table: "CreditAudit",
            column: "InvoiceId");

        migrationBuilder.CreateIndex(
            name: "IX_CreditAudit_PartyId",
            schema: "sales",
            table: "CreditAudit",
            column: "PartyId");

        migrationBuilder.CreateIndex(
            name: "IX_CurrencyAudit_CompanyId_ActorUserId",
            schema: "system",
            table: "CurrencyAudit",
            columns: KeyColumns005);

        migrationBuilder.CreateIndex(
            name: "IX_CurrencyAudit_CompanyTime",
            schema: "system",
            table: "CurrencyAudit",
            columns: KeyColumns007);

        migrationBuilder.CreateIndex(
            name: "IX_CurrencyAudit_CurrencyCode",
            schema: "system",
            table: "CurrencyAudit",
            column: "CurrencyCode");

        migrationBuilder.CreateIndex(
            name: "IX_CurrencyAudit_PreviousCurrencyCode",
            schema: "system",
            table: "CurrencyAudit",
            column: "PreviousCurrencyCode");

        migrationBuilder.CreateIndex(
            name: "IX_DocumentLines_DocumentId_ItemId",
            schema: "inventory",
            table: "DocumentLines",
            columns: KeyColumns020,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_DocumentLines_ItemId",
            schema: "inventory",
            table: "DocumentLines",
            column: "ItemId");

        migrationBuilder.CreateIndex(
            name: "IX_DocumentLines_UnitId",
            schema: "inventory",
            table: "DocumentLines",
            column: "UnitId");

        migrationBuilder.CreateIndex(
            name: "IX_DocumentPatterns_CompanyActive",
            schema: "patterns",
            table: "DocumentPatterns",
            columns: KeyColumns021);

        migrationBuilder.CreateIndex(
            name: "UQ_DocumentPatterns_CompanyOperationCode",
            schema: "patterns",
            table: "DocumentPatterns",
            columns: KeyColumns022,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Documents_CompanyId_BranchId_CreationKey",
            schema: "inventory",
            table: "Documents",
            columns: KeyColumns023,
            unique: true,
            filter: "[CreationKey] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Documents_CompanyId_BranchId_DateUtc",
            schema: "inventory",
            table: "Documents",
            columns: KeyColumns024);

        migrationBuilder.CreateIndex(
            name: "IX_Documents_CompanyId_BranchId_Number",
            schema: "inventory",
            table: "Documents",
            columns: KeyColumns025,
            unique: true,
            filter: "[Number] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Documents_CompanyId_BranchId_PostKey",
            schema: "inventory",
            table: "Documents",
            columns: KeyColumns026,
            unique: true,
            filter: "[PostKey] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Documents_CompanyId_BranchId_ReverseKey",
            schema: "inventory",
            table: "Documents",
            columns: KeyColumns027,
            unique: true,
            filter: "[ReverseKey] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Documents_CompanyId_CreatedBy",
            schema: "inventory",
            table: "Documents",
            columns: KeyColumns028);

        migrationBuilder.CreateIndex(
            name: "IX_Documents_CompanyId_PostedBy",
            schema: "inventory",
            table: "Documents",
            columns: KeyColumns029);

        migrationBuilder.CreateIndex(
            name: "IX_Documents_CompanyId_ReversedBy",
            schema: "inventory",
            table: "Documents",
            columns: KeyColumns030);

        migrationBuilder.CreateIndex(
            name: "IX_Documents_CostCenterId",
            schema: "inventory",
            table: "Documents",
            column: "CostCenterId");

        migrationBuilder.CreateIndex(
            name: "IX_Documents_InventoryAccountId",
            schema: "inventory",
            table: "Documents",
            column: "InventoryAccountId");

        migrationBuilder.CreateIndex(
            name: "IX_Documents_JournalId",
            schema: "inventory",
            table: "Documents",
            column: "JournalId");

        migrationBuilder.CreateIndex(
            name: "IX_Documents_OffsetAccountId",
            schema: "inventory",
            table: "Documents",
            column: "OffsetAccountId");

        migrationBuilder.CreateIndex(
            name: "IX_Documents_ReversalJournalId",
            schema: "inventory",
            table: "Documents",
            column: "ReversalJournalId");

        migrationBuilder.CreateIndex(
            name: "IX_Documents_TargetWarehouseId",
            schema: "inventory",
            table: "Documents",
            column: "TargetWarehouseId");

        migrationBuilder.CreateIndex(
            name: "IX_Documents_WarehouseId",
            schema: "inventory",
            table: "Documents",
            column: "WarehouseId");

        migrationBuilder.CreateIndex(
            name: "IX_ExchangeRates_BaseCurrencyCode",
            schema: "system",
            table: "ExchangeRates",
            column: "BaseCurrencyCode");

        migrationBuilder.CreateIndex(
            name: "IX_ExchangeRates_CompanyCurrencyEffective",
            schema: "system",
            table: "ExchangeRates",
            columns: KeyColumns031);

        migrationBuilder.CreateIndex(
            name: "IX_ExchangeRates_CompanyId_CreatedByUserId",
            schema: "system",
            table: "ExchangeRates",
            columns: KeyColumns016);

        migrationBuilder.CreateIndex(
            name: "IX_ExchangeRates_CurrencyCode",
            schema: "system",
            table: "ExchangeRates",
            column: "CurrencyCode");

        migrationBuilder.CreateIndex(
            name: "IX_FiscalYears_CompanyStart",
            schema: "ledger",
            table: "FiscalYears",
            columns: KeyColumns032);

        migrationBuilder.CreateIndex(
            name: "UQ_FiscalYears_CompanyCode",
            schema: "ledger",
            table: "FiscalYears",
            columns: KeyColumns011,
            unique: true);

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
            name: "IX_SalesInvoiceLines_SourceLineId",
            schema: "sales",
            table: "InvoiceLines",
            column: "SourceLineId",
            filter: "[SourceLineId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Invoices_CompanyId_CreatedByUserId",
            schema: "purchases",
            table: "Invoices",
            columns: KeyColumns016);

        migrationBuilder.CreateIndex(
            name: "IX_Invoices_CompanyId_PostedByUserId",
            schema: "purchases",
            table: "Invoices",
            columns: KeyColumns033);

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
            name: "IX_Invoices_VarianceAccountId",
            schema: "purchases",
            table: "Invoices",
            column: "VarianceAccountId");

        migrationBuilder.CreateIndex(
            name: "IX_Invoices_WarehouseId",
            schema: "purchases",
            table: "Invoices",
            column: "WarehouseId");

        migrationBuilder.CreateIndex(
            name: "IX_PurchaseInvoices_Company_Supplier",
            schema: "purchases",
            table: "Invoices",
            columns: KeyColumns034);

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
            columns: KeyColumns035);

        migrationBuilder.CreateIndex(
            name: "UQ_PurchaseInvoices_Number",
            schema: "purchases",
            table: "Invoices",
            columns: KeyColumns036,
            unique: true,
            filter: "[InvoiceNumber] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "UQ_PurchaseInvoices_PostingKey",
            schema: "purchases",
            table: "Invoices",
            columns: KeyColumns037,
            unique: true,
            filter: "[PostingKey] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Invoices_CompanyId_ApprovedByUserId",
            schema: "sales",
            table: "Invoices",
            columns: KeyColumns038);

        migrationBuilder.CreateIndex(
            name: "IX_Invoices_CompanyId_CreatedByUserId",
            schema: "sales",
            table: "Invoices",
            columns: KeyColumns016);

        migrationBuilder.CreateIndex(
            name: "IX_Invoices_CompanyId_PostedByUserId",
            schema: "sales",
            table: "Invoices",
            columns: KeyColumns033);

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
            name: "IX_Invoices_JournalEntryId",
            schema: "sales",
            table: "Invoices",
            column: "JournalEntryId",
            unique: true,
            filter: "[JournalEntryId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Invoices_PatternRevisionId",
            schema: "sales",
            table: "Invoices",
            column: "PatternRevisionId");

        migrationBuilder.CreateIndex(
            name: "IX_Invoices_ReversedByInvoiceId",
            schema: "sales",
            table: "Invoices",
            column: "ReversedByInvoiceId");

        migrationBuilder.CreateIndex(
            name: "IX_Invoices_ReversesInvoiceId",
            schema: "sales",
            table: "Invoices",
            column: "ReversesInvoiceId",
            unique: true,
            filter: "[ReversesInvoiceId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Invoices_WarehouseId",
            schema: "sales",
            table: "Invoices",
            column: "WarehouseId");

        migrationBuilder.CreateIndex(
            name: "IX_SalesInvoices_Company_Customer",
            schema: "sales",
            table: "Invoices",
            columns: KeyColumns039);

        migrationBuilder.CreateIndex(
            name: "IX_SalesInvoices_Company_Date",
            schema: "sales",
            table: "Invoices",
            columns: KeyColumns040);

        migrationBuilder.CreateIndex(
            name: "IX_SalesInvoices_Company_Status",
            schema: "sales",
            table: "Invoices",
            columns: KeyColumns041);

        migrationBuilder.CreateIndex(
            name: "IX_SalesInvoices_ReturnOf",
            schema: "sales",
            table: "Invoices",
            column: "ReturnOfInvoiceId",
            filter: "[ReturnOfInvoiceId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "UQ_SalesInvoices_Number",
            schema: "sales",
            table: "Invoices",
            columns: KeyColumns036,
            unique: true,
            filter: "[InvoiceNumber] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_ItemBarcodes_CompanyItem",
            schema: "inventory",
            table: "ItemBarcodes",
            columns: KeyColumns042);

        migrationBuilder.CreateIndex(
            name: "IX_ItemBarcodes_ItemId",
            schema: "inventory",
            table: "ItemBarcodes",
            column: "ItemId");

        migrationBuilder.CreateIndex(
            name: "IX_ItemBarcodes_UnitId",
            schema: "inventory",
            table: "ItemBarcodes",
            column: "UnitId");

        migrationBuilder.CreateIndex(
            name: "UQ_ItemBarcodes_CompanyBarcode",
            schema: "inventory",
            table: "ItemBarcodes",
            columns: KeyColumns043,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ItemGroups_CompanyParent",
            schema: "inventory",
            table: "ItemGroups",
            columns: KeyColumns044);

        migrationBuilder.CreateIndex(
            name: "IX_ItemGroups_ParentId",
            schema: "inventory",
            table: "ItemGroups",
            column: "ParentId");

        migrationBuilder.CreateIndex(
            name: "UQ_ItemGroups_CompanyCode",
            schema: "inventory",
            table: "ItemGroups",
            columns: KeyColumns011,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ItemPrices_ItemId",
            schema: "inventory",
            table: "ItemPrices",
            column: "ItemId");

        migrationBuilder.CreateIndex(
            name: "IX_ItemPrices_PartyId",
            schema: "inventory",
            table: "ItemPrices",
            column: "PartyId");

        migrationBuilder.CreateIndex(
            name: "IX_ItemPrices_PatternId",
            schema: "inventory",
            table: "ItemPrices",
            column: "PatternId");

        migrationBuilder.CreateIndex(
            name: "IX_ItemPrices_UnitId",
            schema: "inventory",
            table: "ItemPrices",
            column: "UnitId");

        migrationBuilder.CreateIndex(
            name: "UQ_ItemPrices_Scope",
            schema: "inventory",
            table: "ItemPrices",
            columns: KeyColumns045,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Items_BaseUnitId",
            schema: "inventory",
            table: "Items",
            column: "BaseUnitId");

        migrationBuilder.CreateIndex(
            name: "IX_Items_CompanyGroupActive",
            schema: "inventory",
            table: "Items",
            columns: KeyColumns046);

        migrationBuilder.CreateIndex(
            name: "IX_Items_CompanyNormalizedSearchText",
            schema: "inventory",
            table: "Items",
            columns: KeyColumns047);

        migrationBuilder.CreateIndex(
            name: "IX_Items_DefaultWarehouseId",
            schema: "inventory",
            table: "Items",
            column: "DefaultWarehouseId");

        migrationBuilder.CreateIndex(
            name: "IX_Items_GroupId",
            schema: "inventory",
            table: "Items",
            column: "GroupId");

        migrationBuilder.CreateIndex(
            name: "UQ_Items_CompanyCode",
            schema: "inventory",
            table: "Items",
            columns: KeyColumns011,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_JournalAudit_CompanyId_ActorUserId",
            schema: "ledger",
            table: "JournalAudit",
            columns: KeyColumns005);

        migrationBuilder.CreateIndex(
            name: "IX_JournalAudit_CompanyJournalRevision",
            schema: "ledger",
            table: "JournalAudit",
            columns: KeyColumns048);

        migrationBuilder.CreateIndex(
            name: "UQ_JournalAudit_JournalRevision",
            schema: "ledger",
            table: "JournalAudit",
            columns: KeyColumns049,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_JournalEntries_CompanyBranchStateDate",
            schema: "ledger",
            table: "JournalEntries",
            columns: KeyColumns050);

        migrationBuilder.CreateIndex(
            name: "IX_JournalEntries_CompanyId_PostingFiscalYearId_PostingPeriodId",
            schema: "ledger",
            table: "JournalEntries",
            columns: KeyColumns051);

        migrationBuilder.CreateIndex(
            name: "UQ_JournalEntries_PostingNumber",
            schema: "ledger",
            table: "JournalEntries",
            columns: KeyColumns052,
            unique: true,
            filter: "[PostingSequence] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "UQ_JournalEntries_ReversesEntry",
            schema: "ledger",
            table: "JournalEntries",
            column: "ReversesEntryId",
            unique: true,
            filter: "[ReversesEntryId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_JournalLines_AccountId",
            schema: "ledger",
            table: "JournalLines",
            column: "AccountId");

        migrationBuilder.CreateIndex(
            name: "IX_JournalLines_BaseCurrencyCode",
            schema: "ledger",
            table: "JournalLines",
            column: "BaseCurrencyCode");

        migrationBuilder.CreateIndex(
            name: "IX_JournalLines_CompanyAccountJournal",
            schema: "ledger",
            table: "JournalLines",
            columns: KeyColumns053);

        migrationBuilder.CreateIndex(
            name: "IX_JournalLines_CompanyCostCenter",
            schema: "ledger",
            table: "JournalLines",
            columns: KeyColumns054,
            filter: "[CostCenterId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_JournalLines_CostCenterId",
            schema: "ledger",
            table: "JournalLines",
            column: "CostCenterId");

        migrationBuilder.CreateIndex(
            name: "IX_JournalLines_JournalId",
            schema: "ledger",
            table: "JournalLines",
            column: "JournalId");

        migrationBuilder.CreateIndex(
            name: "IX_JournalLines_TransactionCurrencyCode",
            schema: "ledger",
            table: "JournalLines",
            column: "TransactionCurrencyCode");

        migrationBuilder.CreateIndex(
            name: "IX_Parties_CompanyNormalizedSearchText",
            schema: "parties",
            table: "Parties",
            columns: KeyColumns047);

        migrationBuilder.CreateIndex(
            name: "IX_Parties_CompanyRoleActive",
            schema: "parties",
            table: "Parties",
            columns: KeyColumns055);

        migrationBuilder.CreateIndex(
            name: "IX_Parties_PayableAccountId",
            schema: "parties",
            table: "Parties",
            column: "PayableAccountId");

        migrationBuilder.CreateIndex(
            name: "IX_Parties_ReceivableAccountId",
            schema: "parties",
            table: "Parties",
            column: "ReceivableAccountId");

        migrationBuilder.CreateIndex(
            name: "UQ_Parties_CashCustomer",
            schema: "parties",
            table: "Parties",
            columns: KeyColumns056,
            unique: true,
            filter: "[IsCashCustomer]=1");

        migrationBuilder.CreateIndex(
            name: "UQ_Parties_CompanyCode",
            schema: "parties",
            table: "Parties",
            columns: KeyColumns011,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_PatternBranchGrants_BranchId",
            schema: "patterns",
            table: "PatternBranchGrants",
            column: "BranchId");

        migrationBuilder.CreateIndex(
            name: "UQ_PatternBranchGrants_PatternBranch",
            schema: "patterns",
            table: "PatternBranchGrants",
            columns: KeyColumns057,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_PatternNumberSequences_PatternId",
            schema: "patterns",
            table: "PatternNumberSequences",
            column: "PatternId");

        migrationBuilder.CreateIndex(
            name: "IX_PatternRevisions_CreatedByUserId",
            schema: "patterns",
            table: "PatternRevisions",
            column: "CreatedByUserId");

        migrationBuilder.CreateIndex(
            name: "UQ_PatternRevisions_PatternRevisionNumber",
            schema: "patterns",
            table: "PatternRevisions",
            columns: KeyColumns058,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_PeriodAudit_CompanyId_ActorUserId",
            schema: "ledger",
            table: "PeriodAudit",
            columns: KeyColumns005);

        migrationBuilder.CreateIndex(
            name: "IX_PeriodAudit_CompanyId_FiscalYearId_PeriodId",
            schema: "ledger",
            table: "PeriodAudit",
            columns: KeyColumns059);

        migrationBuilder.CreateIndex(
            name: "IX_PeriodAudit_CompanyPeriodTime",
            schema: "ledger",
            table: "PeriodAudit",
            columns: KeyColumns060);

        migrationBuilder.CreateIndex(
            name: "IX_PolicyAudit_CompanyId_ActorId",
            schema: "parties",
            table: "PolicyAudit",
            columns: KeyColumns061);

        migrationBuilder.CreateIndex(
            name: "IX_PolicyAudit_CompanyId_PartyId_DateUtc",
            schema: "parties",
            table: "PolicyAudit",
            columns: KeyColumns062);

        migrationBuilder.CreateIndex(
            name: "IX_PolicyAudit_PartyId",
            schema: "parties",
            table: "PolicyAudit",
            column: "PartyId");

        migrationBuilder.CreateIndex(
            name: "IX_PostingRequests_CompanyBranchJournal",
            schema: "ledger",
            table: "PostingRequests",
            columns: KeyColumns063);

        migrationBuilder.CreateIndex(
            name: "IX_PostingRequests_CompanyId_ActorUserId",
            schema: "ledger",
            table: "PostingRequests",
            columns: KeyColumns005);

        migrationBuilder.CreateIndex(
            name: "IX_PostingRequests_CompanyId_ActorUserId",
            schema: "sales",
            table: "PostingRequests",
            columns: KeyColumns005);

        migrationBuilder.CreateIndex(
            name: "IX_PostingRequests_CompanyId_BranchId_InvoiceId",
            schema: "sales",
            table: "PostingRequests",
            columns: KeyColumns019);

        migrationBuilder.CreateIndex(
            name: "IX_PostingRequests_InvoiceId",
            schema: "sales",
            table: "PostingRequests",
            column: "InvoiceId");

        migrationBuilder.CreateIndex(
            name: "IX_PostingRequests_OutcomeJournalId",
            schema: "sales",
            table: "PostingRequests",
            column: "OutcomeJournalId");

        migrationBuilder.CreateIndex(
            name: "IX_PostingSequences_CompanyId_FiscalYearId",
            schema: "ledger",
            table: "PostingSequences",
            columns: KeyColumns064);

        migrationBuilder.CreateIndex(
            name: "IX_SettlementApplications_AccountId",
            schema: "treasury",
            table: "SettlementApplications",
            column: "AccountId");

        migrationBuilder.CreateIndex(
            name: "IX_SettlementApplications_BatchId_PurchaseInvoiceId",
            schema: "treasury",
            table: "SettlementApplications",
            columns: KeyColumns065,
            unique: true,
            filter: "[PurchaseInvoiceId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_SettlementApplications_BatchId_SalesInvoiceId",
            schema: "treasury",
            table: "SettlementApplications",
            columns: KeyColumns066,
            unique: true,
            filter: "[SalesInvoiceId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_SettlementApplications_PurchaseInvoiceId",
            schema: "treasury",
            table: "SettlementApplications",
            column: "PurchaseInvoiceId");

        migrationBuilder.CreateIndex(
            name: "IX_SettlementApplications_SalesInvoiceId",
            schema: "treasury",
            table: "SettlementApplications",
            column: "SalesInvoiceId");

        migrationBuilder.CreateIndex(
            name: "IX_SettlementApplications_VoucherId",
            schema: "treasury",
            table: "SettlementApplications",
            column: "VoucherId");

        migrationBuilder.CreateIndex(
            name: "IX_SettlementBatches_CompanyId_ActorId",
            schema: "treasury",
            table: "SettlementBatches",
            columns: KeyColumns061);

        migrationBuilder.CreateIndex(
            name: "IX_SettlementBatches_CompanyId_BranchId_RequestKey",
            schema: "treasury",
            table: "SettlementBatches",
            columns: KeyColumns067,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_SettlementBatches_CompanyId_ReversedBy",
            schema: "treasury",
            table: "SettlementBatches",
            columns: KeyColumns030);

        migrationBuilder.CreateIndex(
            name: "IX_SettlementBatches_JournalId",
            schema: "treasury",
            table: "SettlementBatches",
            column: "JournalId");

        migrationBuilder.CreateIndex(
            name: "IX_SettlementBatches_ReversalJournalId",
            schema: "treasury",
            table: "SettlementBatches",
            column: "ReversalJournalId");

        migrationBuilder.CreateIndex(
            name: "IX_SettlementBatches_VoucherId_DateUtc",
            schema: "treasury",
            table: "SettlementBatches",
            columns: KeyColumns068);

        migrationBuilder.CreateIndex(
            name: "IX_StockAdjustmentLines_Adjustment",
            schema: "inventory",
            table: "StockAdjustmentLines",
            column: "AdjustmentId");

        migrationBuilder.CreateIndex(
            name: "IX_StockAdjustmentLines_ItemId",
            schema: "inventory",
            table: "StockAdjustmentLines",
            column: "ItemId");

        migrationBuilder.CreateIndex(
            name: "IX_StockAdjustmentLines_UnitId",
            schema: "inventory",
            table: "StockAdjustmentLines",
            column: "UnitId");

        migrationBuilder.CreateIndex(
            name: "IX_StockAdjustments_CompanyId_ApprovedByUserId",
            schema: "inventory",
            table: "StockAdjustments",
            columns: KeyColumns038);

        migrationBuilder.CreateIndex(
            name: "IX_StockAdjustments_CompanyId_BranchId",
            schema: "inventory",
            table: "StockAdjustments",
            columns: KeyColumns069);

        migrationBuilder.CreateIndex(
            name: "IX_StockAdjustments_CompanyId_CreatedByUserId",
            schema: "inventory",
            table: "StockAdjustments",
            columns: KeyColumns016);

        migrationBuilder.CreateIndex(
            name: "IX_StockAdjustments_WarehouseId",
            schema: "inventory",
            table: "StockAdjustments",
            column: "WarehouseId");

        migrationBuilder.CreateIndex(
            name: "UQ_StockAdjustments_CompanyAdjustmentNumber",
            schema: "inventory",
            table: "StockAdjustments",
            columns: KeyColumns070,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_StockMovements_CompanyDocument",
            schema: "inventory",
            table: "StockMovements",
            columns: KeyColumns071);

        migrationBuilder.CreateIndex(
            name: "IX_StockMovements_CompanyId_BranchId",
            schema: "inventory",
            table: "StockMovements",
            columns: KeyColumns069);

        migrationBuilder.CreateIndex(
            name: "IX_StockMovements_CompanyId_CreatedByUserId",
            schema: "inventory",
            table: "StockMovements",
            columns: KeyColumns016);

        migrationBuilder.CreateIndex(
            name: "IX_StockMovements_CompanyItemDate",
            schema: "inventory",
            table: "StockMovements",
            columns: KeyColumns072);

        migrationBuilder.CreateIndex(
            name: "IX_StockMovements_CompanyWarehouseItem",
            schema: "inventory",
            table: "StockMovements",
            columns: KeyColumns073);

        migrationBuilder.CreateIndex(
            name: "IX_StockMovements_CostCenterId",
            schema: "inventory",
            table: "StockMovements",
            column: "CostCenterId");

        migrationBuilder.CreateIndex(
            name: "IX_StockMovements_ItemId",
            schema: "inventory",
            table: "StockMovements",
            column: "ItemId");

        migrationBuilder.CreateIndex(
            name: "IX_StockMovements_RecordedSequence",
            schema: "inventory",
            table: "StockMovements",
            column: "RecordedSequence",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_StockMovements_ReversesMovementId",
            schema: "inventory",
            table: "StockMovements",
            column: "ReversesMovementId",
            unique: true,
            filter: "[ReversesMovementId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_StockMovements_UnitId",
            schema: "inventory",
            table: "StockMovements",
            column: "UnitId");

        migrationBuilder.CreateIndex(
            name: "IX_StockMovements_WarehouseId",
            schema: "inventory",
            table: "StockMovements",
            column: "WarehouseId");

        migrationBuilder.CreateIndex(
            name: "UQ_StockMovements_DocumentLine",
            schema: "inventory",
            table: "StockMovements",
            columns: KeyColumns074,
            unique: true,
            filter: "[DocumentLineId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_StockTransferLines_ItemId",
            schema: "inventory",
            table: "StockTransferLines",
            column: "ItemId");

        migrationBuilder.CreateIndex(
            name: "IX_StockTransferLines_Transfer",
            schema: "inventory",
            table: "StockTransferLines",
            column: "TransferId");

        migrationBuilder.CreateIndex(
            name: "IX_StockTransferLines_UnitId",
            schema: "inventory",
            table: "StockTransferLines",
            column: "UnitId");

        migrationBuilder.CreateIndex(
            name: "IX_StockTransfers_CompanyId_CreatedByUserId",
            schema: "inventory",
            table: "StockTransfers",
            columns: KeyColumns016);

        migrationBuilder.CreateIndex(
            name: "IX_StockTransfers_CompanyId_SourceBranchId",
            schema: "inventory",
            table: "StockTransfers",
            columns: KeyColumns075);

        migrationBuilder.CreateIndex(
            name: "IX_StockTransfers_CompanyId_TargetBranchId",
            schema: "inventory",
            table: "StockTransfers",
            columns: KeyColumns076);

        migrationBuilder.CreateIndex(
            name: "IX_StockTransfers_SourceWarehouseId",
            schema: "inventory",
            table: "StockTransfers",
            column: "SourceWarehouseId");

        migrationBuilder.CreateIndex(
            name: "IX_StockTransfers_TargetWarehouseId",
            schema: "inventory",
            table: "StockTransfers",
            column: "TargetWarehouseId");

        migrationBuilder.CreateIndex(
            name: "UQ_StockTransfers_CompanyTransferNumber",
            schema: "inventory",
            table: "StockTransfers",
            columns: KeyColumns077,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_UnitConversions_FromUnitId",
            schema: "inventory",
            table: "UnitConversions",
            column: "FromUnitId");

        migrationBuilder.CreateIndex(
            name: "IX_UnitConversions_ItemId",
            schema: "inventory",
            table: "UnitConversions",
            column: "ItemId");

        migrationBuilder.CreateIndex(
            name: "IX_UnitConversions_ToUnitId",
            schema: "inventory",
            table: "UnitConversions",
            column: "ToUnitId");

        migrationBuilder.CreateIndex(
            name: "UQ_UnitConversions_CompanyItemFromTo",
            schema: "inventory",
            table: "UnitConversions",
            columns: KeyColumns078,
            unique: true,
            filter: "[ItemId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "UQ_UnitsOfMeasure_CompanyCode",
            schema: "inventory",
            table: "UnitsOfMeasure",
            columns: KeyColumns011,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_VoucherAllocations_SalesInvoiceId",
            schema: "treasury",
            table: "VoucherAllocations",
            column: "SalesInvoiceId");

        migrationBuilder.CreateIndex(
            name: "UQ_VoucherAllocations_Invoice",
            schema: "treasury",
            table: "VoucherAllocations",
            columns: KeyColumns013,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_VoucherLines_AccountId",
            schema: "treasury",
            table: "VoucherLines",
            column: "AccountId");

        migrationBuilder.CreateIndex(
            name: "IX_VoucherLines_CostCenterId",
            schema: "treasury",
            table: "VoucherLines",
            column: "CostCenterId");

        migrationBuilder.CreateIndex(
            name: "IX_VoucherLines_PartyId",
            schema: "treasury",
            table: "VoucherLines",
            column: "PartyId");

        migrationBuilder.CreateIndex(
            name: "IX_VoucherLines_VoucherId",
            schema: "treasury",
            table: "VoucherLines",
            column: "VoucherId");

        migrationBuilder.CreateIndex(
            name: "IX_Vouchers_Company_Party",
            schema: "treasury",
            table: "Vouchers",
            columns: KeyColumns079);

        migrationBuilder.CreateIndex(
            name: "IX_Vouchers_CompanyId_ApprovedBy",
            schema: "treasury",
            table: "Vouchers",
            columns: KeyColumns080);

        migrationBuilder.CreateIndex(
            name: "IX_Vouchers_CompanyId_CreatedByUserId",
            schema: "treasury",
            table: "Vouchers",
            columns: KeyColumns016);

        migrationBuilder.CreateIndex(
            name: "IX_Vouchers_CompanyId_PostedByUserId",
            schema: "treasury",
            table: "Vouchers",
            columns: KeyColumns033);

        migrationBuilder.CreateIndex(
            name: "IX_Vouchers_JournalEntryId",
            schema: "treasury",
            table: "Vouchers",
            column: "JournalEntryId",
            unique: true,
            filter: "[JournalEntryId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Vouchers_PartyControlAccountId",
            schema: "treasury",
            table: "Vouchers",
            column: "PartyControlAccountId");

        migrationBuilder.CreateIndex(
            name: "IX_Vouchers_PartyId",
            schema: "treasury",
            table: "Vouchers",
            column: "PartyId");

        migrationBuilder.CreateIndex(
            name: "IX_Vouchers_PatternRevisionId",
            schema: "treasury",
            table: "Vouchers",
            column: "PatternRevisionId");

        migrationBuilder.CreateIndex(
            name: "IX_Vouchers_ReversalJournalEntryId",
            schema: "treasury",
            table: "Vouchers",
            column: "ReversalJournalEntryId",
            unique: true,
            filter: "[ReversalJournalEntryId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Vouchers_Scope_Kind_Date",
            schema: "treasury",
            table: "Vouchers",
            columns: KeyColumns081);

        migrationBuilder.CreateIndex(
            name: "UQ_Vouchers_Number",
            schema: "treasury",
            table: "Vouchers",
            columns: KeyColumns082,
            unique: true,
            filter: "[VoucherNumber] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "UQ_Vouchers_PostingKey",
            schema: "treasury",
            table: "Vouchers",
            columns: KeyColumns037,
            unique: true,
            filter: "[PostingKey] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Warehouses_CompanyBranchActive",
            schema: "inventory",
            table: "Warehouses",
            columns: KeyColumns083);

        migrationBuilder.CreateIndex(
            name: "UQ_Warehouses_CompanyCode",
            schema: "inventory",
            table: "Warehouses",
            columns: KeyColumns011,
            unique: true);
        InstallGuards(migrationBuilder);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("The independent accounting baseline cannot be removed automatically. Restore from an approved backup instead.");
    }
}
