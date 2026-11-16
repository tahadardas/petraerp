using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetraERP.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class LedgerFoundation : Migration
{
    private static readonly string[] PeriodDateRangeColumns = ["CompanyId", "RangeStart", "RangeEnd"];
    private static readonly string[] PeriodYearCodeColumns = ["FiscalYearId", "Code"];
    private static readonly string[] AccountActivePostingColumns = ["CompanyId", "IsActive", "PostingAllowed"];
    private static readonly string[] AccountCompanyCodeColumns = ["CompanyId", "Code"];
    private static readonly string[] CostCenterCompanyCodeColumns = ["CompanyId", "Code"];
    private static readonly string[] FiscalYearCompanyStartColumns = ["CompanyId", "RangeStart"];
    private static readonly string[] FiscalYearCompanyCodeColumns = ["CompanyId", "Code"];
    private static readonly string[] AuditCompanyJournalRevisionColumns = ["CompanyId", "JournalId", "Revision"];
    private static readonly string[] AuditJournalRevisionColumns = ["JournalId", "Revision"];
    private static readonly string[] JournalStateColumns = ["CompanyId", "BranchId", "State", "AccountingDate"];
    private static readonly string[] JournalPostingNumberColumns = ["CompanyId", "BranchId", "PostingFiscalYearId", "PostingSequence"];
    private static readonly string[] LineCompanyAccountJournalColumns = ["CompanyId", "AccountId", "JournalId"];
    private static readonly string[] LineCompanyCostCenterColumns = ["CompanyId", "CostCenterId"];
    private static readonly string[] PostingRequestCompanyBranchJournalColumns = ["CompanyId", "BranchId", "JournalId"];
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ledger");

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
                    table.CheckConstraint("CK_FiscalYears_Code", "DATALENGTH([Code]) > 0 AND DATALENGTH([Code]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Code]))");
                    table.CheckConstraint("CK_FiscalYears_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_FiscalYears_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_FiscalYears_Range", "[RangeEnd] > [RangeStart]");
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
                name: "PostingRequests",
                schema: "ledger",
                columns: table => new
                {
                    IdempotencyKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Operation = table.Column<byte>(type: "tinyint", nullable: false),
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
                    table.PrimaryKey("PK_PostingRequests", x => x.IdempotencyKey);
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
                    table.CheckConstraint("CK_AccountingPeriods_Code", "DATALENGTH([Code]) > 0 AND DATALENGTH([Code]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Code]))");
                    table.CheckConstraint("CK_AccountingPeriods_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_AccountingPeriods_FiscalYearId", "[FiscalYearId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_AccountingPeriods_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_AccountingPeriods_Range", "[RangeEnd] > [RangeStart]");
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
                        name: "FK_AccountingPeriods_FiscalYears",
                        column: x => x.FiscalYearId,
                        principalSchema: "ledger",
                        principalTable: "FiscalYears",
                        principalColumn: "Id",
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
                        name: "FK_PostingSequences_Branches",
                        column: x => x.BranchId,
                        principalSchema: "system",
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PostingSequences_Companies",
                        column: x => x.CompanyId,
                        principalSchema: "system",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PostingSequences_FiscalYears",
                        column: x => x.FiscalYearId,
                        principalSchema: "ledger",
                        principalTable: "FiscalYears",
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
                        name: "FK_JournalEntries_Branches",
                        column: x => x.BranchId,
                        principalSchema: "system",
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JournalEntries_Companies",
                        column: x => x.CompanyId,
                        principalSchema: "system",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JournalEntries_PostingPeriod",
                        column: x => x.PostingPeriodId,
                        principalSchema: "ledger",
                        principalTable: "AccountingPeriods",
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
                    TransactionDebit = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    TransactionCredit = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    BaseCurrencyCode = table.Column<string>(type: "char(3)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    BaseDebit = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    BaseCredit = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    RateSourceCurrencyCode = table.Column<string>(type: "char(3)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    RateTargetCurrencyCode = table.Column<string>(type: "char(3)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    RateValue = table.Column<decimal>(type: "decimal(18,10)", nullable: false),
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

            migrationBuilder.CreateIndex(
                name: "IX_AccountingPeriods_CompanyDateRange",
                schema: "ledger",
                table: "AccountingPeriods",
                columns: PeriodDateRangeColumns);

            migrationBuilder.CreateIndex(
                name: "UQ_AccountingPeriods_YearCode",
                schema: "ledger",
                table: "AccountingPeriods",
                columns: PeriodYearCodeColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_CompanyActivePosting",
                schema: "ledger",
                table: "Accounts",
                columns: AccountActivePostingColumns);

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
                columns: AccountCompanyCodeColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_CostCenters_CompanyCode",
                schema: "ledger",
                table: "CostCenters",
                columns: CostCenterCompanyCodeColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FiscalYears_CompanyStart",
                schema: "ledger",
                table: "FiscalYears",
                columns: FiscalYearCompanyStartColumns);

            migrationBuilder.CreateIndex(
                name: "UQ_FiscalYears_CompanyCode",
                schema: "ledger",
                table: "FiscalYears",
                columns: FiscalYearCompanyCodeColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalAudit_CompanyJournalRevision",
                schema: "ledger",
                table: "JournalAudit",
                columns: AuditCompanyJournalRevisionColumns);

            migrationBuilder.CreateIndex(
                name: "UQ_JournalAudit_JournalRevision",
                schema: "ledger",
                table: "JournalAudit",
                columns: AuditJournalRevisionColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_BranchId",
                schema: "ledger",
                table: "JournalEntries",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_CompanyBranchStateDate",
                schema: "ledger",
                table: "JournalEntries",
                columns: JournalStateColumns);

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_PostingPeriodId",
                schema: "ledger",
                table: "JournalEntries",
                column: "PostingPeriodId");

            migrationBuilder.CreateIndex(
                name: "UQ_JournalEntries_PostingNumber",
                schema: "ledger",
                table: "JournalEntries",
                columns: JournalPostingNumberColumns,
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
                columns: LineCompanyAccountJournalColumns);

            migrationBuilder.CreateIndex(
                name: "IX_JournalLines_CompanyCostCenter",
                schema: "ledger",
                table: "JournalLines",
                columns: LineCompanyCostCenterColumns,
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
                name: "IX_PostingRequests_CompanyBranchJournal",
                schema: "ledger",
                table: "PostingRequests",
                columns: PostingRequestCompanyBranchJournalColumns);

            migrationBuilder.CreateIndex(
                name: "IX_PostingSequences_BranchId",
                schema: "ledger",
                table: "PostingSequences",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_PostingSequences_FiscalYearId",
                schema: "ledger",
                table: "PostingSequences",
                column: "FiscalYearId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JournalAudit",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "JournalLines",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "PostingRequests",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "PostingSequences",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "Accounts",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "CostCenters",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "JournalEntries",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "AccountingPeriods",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "FiscalYears",
                schema: "ledger");
    }
}
