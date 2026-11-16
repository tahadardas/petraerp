#pragma warning disable IDE0161
#pragma warning disable CA1861

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetraERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccountingCoreCompletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SettlementApplications_Target",
                schema: "treasury",
                table: "SettlementApplications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AllocationPlans_Target",
                schema: "treasury",
                table: "AllocationPlans");

            migrationBuilder.AddColumn<Guid>(
                name: "OpenItemId",
                schema: "treasury",
                table: "SettlementApplications",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OpenItemId",
                schema: "treasury",
                table: "AllocationPlans",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "FxMonetaryEligibility",
                schema: "ledger",
                table: "Accounts",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)1);

            migrationBuilder.CreateTable(
                name: "OpeningBalanceBatches",
                schema: "ledger",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FiscalYearId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OpeningDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SourceReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SourceFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    OriginalCurrency = table.Column<string>(type: "char(3)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    BaseCurrency = table.Column<string>(type: "char(3)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    ApprovalState = table.Column<byte>(type: "tinyint", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovedByActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PostedJournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReversalJournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningBalanceBatches", x => x.Id);
                    table.CheckConstraint("CK_OpeningBalanceBatches_ApprovalState", "[ApprovalState] IN (1, 2, 3, 4, 5)");
                    table.CheckConstraint("CK_OpeningBalanceBatches_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_OpeningBalanceBatches_FiscalYearId", "[FiscalYearId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_OpeningBalanceBatches_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_OpeningBalanceBatches_SourceType", "LEN(LTRIM(RTRIM([SourceType]))) > 0");
                    table.CheckConstraint("CK_OpeningBalanceBatches_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                    table.ForeignKey(
                        name: "FK_OpeningBalanceBatches_Branches",
                        column: x => x.BranchId,
                        principalSchema: "system",
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpeningBalanceBatches_Companies",
                        column: x => x.CompanyId,
                        principalSchema: "system",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpeningBalanceBatches_PostedJournal",
                        column: x => x.PostedJournalId,
                        principalSchema: "ledger",
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpeningBalanceBatches_ReversalJournal",
                        column: x => x.ReversalJournalId,
                        principalSchema: "ledger",
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OpenItems",
                schema: "parties",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PartyType = table.Column<byte>(type: "tinyint", nullable: false),
                    OriginFiscalYearId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OriginDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OriginDocumentNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    OriginDocumentDateUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    DueDateUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    CurrencyCode = table.Column<string>(type: "char(3)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    OriginalAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    SettledAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    OutstandingAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(19,8)", precision: 19, scale: 8, nullable: false),
                    OriginalBaseCarryingAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    CurrentBaseCarryingAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    OriginType = table.Column<byte>(type: "tinyint", nullable: false),
                    SourceReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RolloverBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<byte>(type: "tinyint", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenItems", x => x.Id);
                    table.CheckConstraint("CK_OpenItems_Amounts", "[OriginalAmount] > 0 AND [SettledAmount] >= 0 AND [OutstandingAmount] >= 0 AND [SettledAmount] + [OutstandingAmount] = [OriginalAmount]");
                    table.CheckConstraint("CK_OpenItems_BranchId", "[BranchId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_OpenItems_CarryingAmounts", "[OriginalBaseCarryingAmount] >= 0 AND [CurrentBaseCarryingAmount] >= 0");
                    table.CheckConstraint("CK_OpenItems_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_OpenItems_ExchangeRate", "[ExchangeRate] > 0");
                    table.CheckConstraint("CK_OpenItems_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_OpenItems_OriginType", "[OriginType] IN (1, 2, 3, 4)");
                    table.CheckConstraint("CK_OpenItems_PartyId", "[PartyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_OpenItems_PartyType", "[PartyType] IN (1, 2)");
                    table.CheckConstraint("CK_OpenItems_State", "[State] IN (1, 2, 3, 4)");
                    table.CheckConstraint("CK_OpenItems_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                    table.ForeignKey(
                        name: "FK_OpenItems_Accounts",
                        column: x => x.AccountId,
                        principalSchema: "ledger",
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpenItems_Branches",
                        column: x => x.BranchId,
                        principalSchema: "system",
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpenItems_Companies",
                        column: x => x.CompanyId,
                        principalSchema: "system",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpenItems_Parties",
                        column: x => x.PartyId,
                        principalSchema: "parties",
                        principalTable: "Parties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OpeningBalanceLines",
                schema: "ledger",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CurrencyCode = table.Column<string>(type: "char(3)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    DebitOriginal = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    CreditOriginal = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(19,8)", precision: 19, scale: 8, nullable: false),
                    DebitBase = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    CreditBase = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    LineOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningBalanceLines", x => x.Id);
                    table.CheckConstraint("CK_OpeningBalanceLines_BaseAmounts", "[DebitBase] >= 0 AND [CreditBase] >= 0 AND (([DebitBase] > 0 AND [CreditBase] = 0) OR ([DebitBase] = 0 AND [CreditBase] > 0))");
                    table.CheckConstraint("CK_OpeningBalanceLines_ExchangeRate", "[ExchangeRate] > 0");
                    table.CheckConstraint("CK_OpeningBalanceLines_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_OpeningBalanceLines_LineOrder", "[LineOrder] >= 0");
                    table.CheckConstraint("CK_OpeningBalanceLines_OriginalAmounts", "[DebitOriginal] >= 0 AND [CreditOriginal] >= 0 AND (([DebitOriginal] > 0 AND [CreditOriginal] = 0) OR ([DebitOriginal] = 0 AND [CreditOriginal] > 0))");
                    table.ForeignKey(
                        name: "FK_OpeningBalanceLines_Accounts",
                        column: x => x.AccountId,
                        principalSchema: "ledger",
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpeningBalanceLines_CostCenters",
                        column: x => x.CostCenterId,
                        principalSchema: "ledger",
                        principalTable: "CostCenters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpeningBalanceLines_OpeningBalanceBatches_BatchId",
                        column: x => x.BatchId,
                        principalSchema: "ledger",
                        principalTable: "OpeningBalanceBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SettlementApplications_BatchId_OpenItemId",
                schema: "treasury",
                table: "SettlementApplications",
                columns: new[] { "BatchId", "OpenItemId" },
                unique: true,
                filter: "[OpenItemId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementApplications_OpenItemId",
                schema: "treasury",
                table: "SettlementApplications",
                column: "OpenItemId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SettlementApplications_Target",
                schema: "treasury",
                table: "SettlementApplications",
                sql: "([SalesInvoiceId] IS NOT NULL AND [PurchaseInvoiceId] IS NULL AND [OpenItemId] IS NULL) OR ([SalesInvoiceId] IS NULL AND [PurchaseInvoiceId] IS NOT NULL AND [OpenItemId] IS NULL) OR ([SalesInvoiceId] IS NULL AND [PurchaseInvoiceId] IS NULL AND [OpenItemId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_AllocationPlans_OpenItemId",
                schema: "treasury",
                table: "AllocationPlans",
                column: "OpenItemId");

            migrationBuilder.CreateIndex(
                name: "IX_AllocationPlans_VoucherId_OpenItemId",
                schema: "treasury",
                table: "AllocationPlans",
                columns: new[] { "VoucherId", "OpenItemId" },
                unique: true,
                filter: "[OpenItemId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AllocationPlans_Target",
                schema: "treasury",
                table: "AllocationPlans",
                sql: "([SalesInvoiceId] IS NOT NULL AND [PurchaseInvoiceId] IS NULL AND [OpenItemId] IS NULL) OR ([SalesInvoiceId] IS NULL AND [PurchaseInvoiceId] IS NOT NULL AND [OpenItemId] IS NULL) OR ([SalesInvoiceId] IS NULL AND [PurchaseInvoiceId] IS NULL AND [OpenItemId] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Accounts_FxMonetaryEligibility",
                schema: "ledger",
                table: "Accounts",
                sql: "[FxMonetaryEligibility] IN (1, 2, 3, 4)");

            migrationBuilder.CreateIndex(
                name: "IX_OpeningBalanceBatches_BranchId",
                schema: "ledger",
                table: "OpeningBalanceBatches",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_OpeningBalanceBatches_CompanyYearState",
                schema: "ledger",
                table: "OpeningBalanceBatches",
                columns: new[] { "CompanyId", "FiscalYearId", "ApprovalState" });

            migrationBuilder.CreateIndex(
                name: "IX_OpeningBalanceBatches_PostedJournalId",
                schema: "ledger",
                table: "OpeningBalanceBatches",
                column: "PostedJournalId");

            migrationBuilder.CreateIndex(
                name: "IX_OpeningBalanceBatches_ReversalJournalId",
                schema: "ledger",
                table: "OpeningBalanceBatches",
                column: "ReversalJournalId");

            migrationBuilder.CreateIndex(
                name: "UQ_OpeningBalanceBatches_CompanyIdempotency",
                schema: "ledger",
                table: "OpeningBalanceBatches",
                columns: new[] { "CompanyId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpeningBalanceLines_AccountId",
                schema: "ledger",
                table: "OpeningBalanceLines",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_OpeningBalanceLines_BatchId",
                schema: "ledger",
                table: "OpeningBalanceLines",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_OpeningBalanceLines_CostCenterId",
                schema: "ledger",
                table: "OpeningBalanceLines",
                column: "CostCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_OpenItems_AccountId",
                schema: "parties",
                table: "OpenItems",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_OpenItems_BranchId",
                schema: "parties",
                table: "OpenItems",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_OpenItems_CompanyBranchPartyState",
                schema: "parties",
                table: "OpenItems",
                columns: new[] { "CompanyId", "BranchId", "PartyId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_OpenItems_CompanyPartyOutstanding",
                schema: "parties",
                table: "OpenItems",
                columns: new[] { "CompanyId", "PartyId", "OutstandingAmount" });

            migrationBuilder.CreateIndex(
                name: "IX_OpenItems_PartyId",
                schema: "parties",
                table: "OpenItems",
                column: "PartyId");

            migrationBuilder.AddForeignKey(
                name: "FK_AllocationPlans_OpenItems_OpenItemId",
                schema: "treasury",
                table: "AllocationPlans",
                column: "OpenItemId",
                principalSchema: "parties",
                principalTable: "OpenItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SettlementApplications_OpenItems_OpenItemId",
                schema: "treasury",
                table: "SettlementApplications",
                column: "OpenItemId",
                principalSchema: "parties",
                principalTable: "OpenItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AllocationPlans_OpenItems_OpenItemId",
                schema: "treasury",
                table: "AllocationPlans");

            migrationBuilder.DropForeignKey(
                name: "FK_SettlementApplications_OpenItems_OpenItemId",
                schema: "treasury",
                table: "SettlementApplications");

            migrationBuilder.DropTable(
                name: "OpeningBalanceLines",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "OpenItems",
                schema: "parties");

            migrationBuilder.DropTable(
                name: "OpeningBalanceBatches",
                schema: "ledger");

            migrationBuilder.DropIndex(
                name: "IX_SettlementApplications_BatchId_OpenItemId",
                schema: "treasury",
                table: "SettlementApplications");

            migrationBuilder.DropIndex(
                name: "IX_SettlementApplications_OpenItemId",
                schema: "treasury",
                table: "SettlementApplications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SettlementApplications_Target",
                schema: "treasury",
                table: "SettlementApplications");

            migrationBuilder.DropIndex(
                name: "IX_AllocationPlans_OpenItemId",
                schema: "treasury",
                table: "AllocationPlans");

            migrationBuilder.DropIndex(
                name: "IX_AllocationPlans_VoucherId_OpenItemId",
                schema: "treasury",
                table: "AllocationPlans");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AllocationPlans_Target",
                schema: "treasury",
                table: "AllocationPlans");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Accounts_FxMonetaryEligibility",
                schema: "ledger",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "OpenItemId",
                schema: "treasury",
                table: "SettlementApplications");

            migrationBuilder.DropColumn(
                name: "OpenItemId",
                schema: "treasury",
                table: "AllocationPlans");

            migrationBuilder.DropColumn(
                name: "FxMonetaryEligibility",
                schema: "ledger",
                table: "Accounts");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SettlementApplications_Target",
                schema: "treasury",
                table: "SettlementApplications",
                sql: "([SalesInvoiceId] IS NULL AND [PurchaseInvoiceId] IS NOT NULL) OR ([SalesInvoiceId] IS NOT NULL AND [PurchaseInvoiceId] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AllocationPlans_Target",
                schema: "treasury",
                table: "AllocationPlans",
                sql: "([SalesInvoiceId] IS NULL AND [PurchaseInvoiceId] IS NOT NULL) OR ([SalesInvoiceId] IS NOT NULL AND [PurchaseInvoiceId] IS NULL)");
        }
    }
}
