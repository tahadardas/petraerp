using System;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
#pragma warning disable CA1861
namespace PetraERP.Infrastructure.Persistence.Migrations;

/// <summary>Source only; not applied to any database. Creates treasury.CommercialPapers and the system database-registry tables added by the gap-closure slice.</summary>
public partial class CommercialPapersAndDatabaseRegistry : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "BackupCatalog",
            schema: "system",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DatabaseName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                BackupType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                BackupFilePath = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                ChecksumSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                FirstLsn = table.Column<long>(type: "bigint", nullable: true),
                LastLsn = table.Column<long>(type: "bigint", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                VerifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BackupCatalog", x => x.Id);
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
            name: "CompanyDatabases",
            schema: "system",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DatabaseKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                DatabaseName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CompanyDatabases", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "DatabaseVersions",
            schema: "system",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DatabaseName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                MigrationId = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                ProductVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                AppliedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DatabaseVersions", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "FiscalDatabases",
            schema: "system",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                FiscalYearId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                YearNumber = table.Column<int>(type: "int", nullable: false),
                SafeDatabaseKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                DatabaseName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                State = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                SchemaVersion = table.Column<int>(type: "int", nullable: false),
                IsDefault = table.Column<bool>(type: "bit", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FiscalDatabases", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "GlobalEntityMappings",
            schema: "system",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EntityType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                GlobalEntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LocalEntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                FiscalYearId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GlobalEntityMappings", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "IntercompanyOperations",
            schema: "system",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SourceCompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TargetCompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OperationType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                SourceDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TargetDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                CurrencyCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IntercompanyOperations", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ProvisioningOperations",
            schema: "system",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                FiscalYearId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                OperationType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                TargetDatabaseName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                CurrentStep = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                ErrorDetails = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProvisioningOperations", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_CommercialPapers_BankAccountId",
            schema: "treasury",
            table: "CommercialPapers",
            column: "BankAccountId");

        migrationBuilder.CreateIndex(
            name: "IX_CommercialPapers_CompanyId_BranchId_PaperNumber",
            schema: "treasury",
            table: "CommercialPapers",
            columns: new[] { "CompanyId", "BranchId", "PaperNumber" });

        migrationBuilder.CreateIndex(
            name: "IX_CommercialPapers_CompanyId_BranchId_Status",
            schema: "treasury",
            table: "CommercialPapers",
            columns: new[] { "CompanyId", "BranchId", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_CommercialPapers_CompanyId_DueDate_Status",
            schema: "treasury",
            table: "CommercialPapers",
            columns: new[] { "CompanyId", "DueDate", "Status" });

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
            name: "IX_CompanyDatabases_CompanyId",
            schema: "system",
            table: "CompanyDatabases",
            column: "CompanyId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_CompanyDatabases_DatabaseKey",
            schema: "system",
            table: "CompanyDatabases",
            column: "DatabaseKey",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_DatabaseVersions_DatabaseName_MigrationId",
            schema: "system",
            table: "DatabaseVersions",
            columns: new[] { "DatabaseName", "MigrationId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_FiscalDatabases_CompanyId_FiscalYearId",
            schema: "system",
            table: "FiscalDatabases",
            columns: new[] { "CompanyId", "FiscalYearId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_FiscalDatabases_CompanyId_YearNumber",
            schema: "system",
            table: "FiscalDatabases",
            columns: new[] { "CompanyId", "YearNumber" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_FiscalDatabases_SafeDatabaseKey",
            schema: "system",
            table: "FiscalDatabases",
            column: "SafeDatabaseKey",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_GlobalEntityMappings_CompanyId_EntityType_GlobalEntityId",
            schema: "system",
            table: "GlobalEntityMappings",
            columns: new[] { "CompanyId", "EntityType", "GlobalEntityId" });

        migrationBuilder.CreateIndex(
            name: "IX_GlobalEntityMappings_CompanyId_EntityType_LocalEntityId_FiscalYearId",
            schema: "system",
            table: "GlobalEntityMappings",
            columns: new[] { "CompanyId", "EntityType", "LocalEntityId", "FiscalYearId" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "BackupCatalog",
            schema: "system");

        migrationBuilder.DropTable(
            name: "CommercialPapers",
            schema: "treasury");

        migrationBuilder.DropTable(
            name: "CompanyDatabases",
            schema: "system");

        migrationBuilder.DropTable(
            name: "DatabaseVersions",
            schema: "system");

        migrationBuilder.DropTable(
            name: "FiscalDatabases",
            schema: "system");

        migrationBuilder.DropTable(
            name: "GlobalEntityMappings",
            schema: "system");

        migrationBuilder.DropTable(
            name: "IntercompanyOperations",
            schema: "system");

        migrationBuilder.DropTable(
            name: "ProvisioningOperations",
            schema: "system");
    }
}
