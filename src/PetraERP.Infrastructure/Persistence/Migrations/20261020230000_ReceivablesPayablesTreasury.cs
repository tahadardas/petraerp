using System;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
#pragma warning disable CA1861
namespace PetraERP.Infrastructure.Persistence.Migrations;

/// <summary>Source only. Preserve legacy receipts and distributions; no synthetic historical applications or approval actors.</summary>
public partial class ReceivablesPayablesTreasury : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(name: "IsCashCustomer", schema: "parties", table: "Parties", type: "bit", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<bool>(name: "IsCreditBlocked", schema: "parties", table: "Parties", type: "bit", nullable: false, defaultValue: false);
        migrationBuilder.CreateIndex(name: "UQ_Parties_CashCustomer", schema: "parties", table: "Parties", columns: new[] { "CompanyId", "IsCashCustomer" }, unique: true, filter: "[IsCashCustomer]=1");
        migrationBuilder.AddColumn<Guid>(name: "ApprovedBy", schema: "treasury", table: "Vouchers", type: "uniqueidentifier", nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>(name: "ApprovedAtUtc", schema: "treasury", table: "Vouchers", type: "datetimeoffset(7)", nullable: true, precision: 7);
        migrationBuilder.AddColumn<string>(name: "ApprovalReason", schema: "treasury", table: "Vouchers", type: "nvarchar(500)", nullable: true, maxLength: 500);
        migrationBuilder.AddColumn<Guid>(name: "PartyControlAccountId", schema: "treasury", table: "Vouchers", type: "uniqueidentifier", nullable: true);
        migrationBuilder.CreateIndex(name: "IX_Vouchers_ApprovedBy", schema: "treasury", table: "Vouchers", column: "ApprovedBy");
        migrationBuilder.AddForeignKey(name: "FK_Vouchers_Users_ApprovedBy", schema: "treasury", table: "Vouchers", column: "ApprovedBy", principalSchema: "security", principalTable: "Users", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.CreateIndex(name: "IX_Vouchers_PartyControlAccountId", schema: "treasury", table: "Vouchers", column: "PartyControlAccountId");
        migrationBuilder.AddForeignKey(name: "FK_Vouchers_Accounts_PartyControlAccountId", schema: "treasury", table: "Vouchers", column: "PartyControlAccountId", principalSchema: "ledger", principalTable: "Accounts", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.DropCheckConstraint(name: "CK_Vouchers_Status", schema: "treasury", table: "Vouchers");
        migrationBuilder.AddCheckConstraint(name: "CK_Vouchers_Status", schema: "treasury", table: "Vouchers", sql: "[Status] IN (0, 1, 2, 3)");
        migrationBuilder.DropCheckConstraint(name: "CK_Vouchers_Posted", schema: "treasury", table: "Vouchers");
        migrationBuilder.AddCheckConstraint(name: "CK_Vouchers_Posted", schema: "treasury", table: "Vouchers", sql: "[Status] IN (0,1) OR ([JournalEntryId] IS NOT NULL AND [VoucherNumber] IS NOT NULL AND [PostingKey] IS NOT NULL AND [PostedAtUtc] IS NOT NULL)");
        migrationBuilder.CreateTable(name: "AllocationPlans", schema: "treasury", columns: table => new
        {
            Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            VoucherId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            SalesInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
            PurchaseInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
            InvoiceAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
            SettlementAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
            CrossRate = table.Column<decimal>(type: "decimal(19,8)", precision: 19, scale: 8, nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_AllocationPlans", x => x.Id);
            table.ForeignKey("FK_AllocationPlans_Vouchers_VoucherId", x => x.VoucherId, "Vouchers", "Id", "treasury", onDelete: ReferentialAction.Cascade);
            table.ForeignKey("FK_AllocationPlans_Invoices_SalesInvoiceId", x => x.SalesInvoiceId, "Invoices", "Id", "sales", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_AllocationPlans_Invoices_PurchaseInvoiceId", x => x.PurchaseInvoiceId, "Invoices", "Id", "purchases", onDelete: ReferentialAction.Restrict);
            table.CheckConstraint("CK_AllocationPlans_Target", "([SalesInvoiceId] IS NULL AND [PurchaseInvoiceId] IS NOT NULL) OR ([SalesInvoiceId] IS NOT NULL AND [PurchaseInvoiceId] IS NULL)");
            table.CheckConstraint("CK_AllocationPlans_Amounts", "[InvoiceAmount]>0 AND [SettlementAmount]>0 AND [CrossRate]>0");
        });
        migrationBuilder.CreateIndex(name: "IX_AllocationPlans_VoucherId_SalesInvoiceId", schema: "treasury", table: "AllocationPlans", columns: new[] { "VoucherId", "SalesInvoiceId" }, unique: true, filter: "[SalesInvoiceId] IS NOT NULL");
        migrationBuilder.CreateIndex(name: "IX_AllocationPlans_VoucherId_PurchaseInvoiceId", schema: "treasury", table: "AllocationPlans", columns: new[] { "VoucherId", "PurchaseInvoiceId" }, unique: true, filter: "[PurchaseInvoiceId] IS NOT NULL");
        migrationBuilder.CreateIndex(name: "IX_AllocationPlans_SalesInvoiceId", schema: "treasury", table: "AllocationPlans", columns: new[] { "SalesInvoiceId" });
        migrationBuilder.CreateIndex(name: "IX_AllocationPlans_PurchaseInvoiceId", schema: "treasury", table: "AllocationPlans", columns: new[] { "PurchaseInvoiceId" });
        migrationBuilder.CreateTable(name: "SettlementBatches", schema: "treasury", columns: table => new
        {
            Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            VoucherId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            IsInitial = table.Column<bool>(type: "bit", nullable: false),
            State = table.Column<int>(type: "int", nullable: false),
            DateUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
            Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
            RequestKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, collation: "Latin1_General_100_BIN2", nullable: false),
            Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
            ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
            JournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
            ReversalJournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
            ReversedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
            ReversedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
            ReversalReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
            RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_SettlementBatches", x => x.Id);
            table.ForeignKey("FK_SettlementBatches_Companies_CompanyId", x => x.CompanyId, "Companies", "Id", "system", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_SettlementBatches_Branches_BranchId", x => x.BranchId, "Branches", "Id", "system", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_SettlementBatches_Vouchers_VoucherId", x => x.VoucherId, "Vouchers", "Id", "treasury", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_SettlementBatches_Users_ActorId", x => x.ActorId, "Users", "Id", "security", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_SettlementBatches_JournalEntries_JournalId", x => x.JournalId, "JournalEntries", "Id", "ledger", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_SettlementBatches_JournalEntries_ReversalJournalId", x => x.ReversalJournalId, "JournalEntries", "Id", "ledger", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_SettlementBatches_Users_ReversedBy", x => x.ReversedBy, "Users", "Id", "security", onDelete: ReferentialAction.Restrict);
            table.CheckConstraint("CK_SettlementBatches_State", "[State] IN (1,2)");
            table.CheckConstraint("CK_SettlementBatches_Reason", "LEN(LTRIM(RTRIM([Reason])))>0");
            table.CheckConstraint("CK_SettlementBatches_Reversal", "[State]=1 OR ([ReversedAtUtc] IS NOT NULL AND [ReversedBy] IS NOT NULL AND [ReversalReason] IS NOT NULL)");
            table.CheckConstraint("CK_SettlementBatches_Utc", "DATEPART(TZOFFSET,[DateUtc])=0 AND DATEPART(TZOFFSET,[RecordedAtUtc])=0");
        });
        migrationBuilder.CreateIndex(name: "IX_SettlementBatches_CompanyId_BranchId_RequestKey", schema: "treasury", table: "SettlementBatches", columns: new[] { "CompanyId", "BranchId", "RequestKey" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_SettlementBatches_VoucherId_DateUtc", schema: "treasury", table: "SettlementBatches", columns: new[] { "VoucherId", "DateUtc" });
        migrationBuilder.CreateIndex(name: "IX_SettlementBatches_BranchId", schema: "treasury", table: "SettlementBatches", columns: new[] { "BranchId" });
        migrationBuilder.CreateIndex(name: "IX_SettlementBatches_ActorId", schema: "treasury", table: "SettlementBatches", columns: new[] { "ActorId" });
        migrationBuilder.CreateIndex(name: "IX_SettlementBatches_JournalId", schema: "treasury", table: "SettlementBatches", columns: new[] { "JournalId" });
        migrationBuilder.CreateIndex(name: "IX_SettlementBatches_ReversalJournalId", schema: "treasury", table: "SettlementBatches", columns: new[] { "ReversalJournalId" });
        migrationBuilder.CreateIndex(name: "IX_SettlementBatches_ReversedBy", schema: "treasury", table: "SettlementBatches", columns: new[] { "ReversedBy" });
        migrationBuilder.CreateTable(name: "SettlementApplications", schema: "treasury", columns: table => new
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
        }, constraints: table =>
        {
            table.PrimaryKey("PK_SettlementApplications", x => x.Id);
            table.ForeignKey("FK_SettlementApplications_SettlementBatches_BatchId", x => x.BatchId, "SettlementBatches", "Id", "treasury", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_SettlementApplications_Vouchers_VoucherId", x => x.VoucherId, "Vouchers", "Id", "treasury", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_SettlementApplications_Invoices_SalesInvoiceId", x => x.SalesInvoiceId, "Invoices", "Id", "sales", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_SettlementApplications_Invoices_PurchaseInvoiceId", x => x.PurchaseInvoiceId, "Invoices", "Id", "purchases", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_SettlementApplications_Accounts_AccountId", x => x.AccountId, "Accounts", "Id", "ledger", onDelete: ReferentialAction.Restrict);
            table.CheckConstraint("CK_SettlementApplications_Target", "([SalesInvoiceId] IS NULL AND [PurchaseInvoiceId] IS NOT NULL) OR ([SalesInvoiceId] IS NOT NULL AND [PurchaseInvoiceId] IS NULL)");
            table.CheckConstraint("CK_SettlementApplications_Amounts", "[InvoiceAmount]>0 AND [SettlementAmount]>0 AND [BookedBaseAmount]>=0 AND [SettlementBaseAmount]>=0 AND [InvoiceRate]>0 AND [SettlementRate]>0 AND [CrossRate]>0");
        });
        migrationBuilder.CreateIndex(name: "IX_SettlementApplications_BatchId_SalesInvoiceId", schema: "treasury", table: "SettlementApplications", columns: new[] { "BatchId", "SalesInvoiceId" }, unique: true, filter: "[SalesInvoiceId] IS NOT NULL");
        migrationBuilder.CreateIndex(name: "IX_SettlementApplications_BatchId_PurchaseInvoiceId", schema: "treasury", table: "SettlementApplications", columns: new[] { "BatchId", "PurchaseInvoiceId" }, unique: true, filter: "[PurchaseInvoiceId] IS NOT NULL");
        migrationBuilder.CreateIndex(name: "IX_SettlementApplications_VoucherId", schema: "treasury", table: "SettlementApplications", columns: new[] { "VoucherId" });
        migrationBuilder.CreateIndex(name: "IX_SettlementApplications_SalesInvoiceId", schema: "treasury", table: "SettlementApplications", columns: new[] { "SalesInvoiceId" });
        migrationBuilder.CreateIndex(name: "IX_SettlementApplications_PurchaseInvoiceId", schema: "treasury", table: "SettlementApplications", columns: new[] { "PurchaseInvoiceId" });
        migrationBuilder.CreateIndex(name: "IX_SettlementApplications_AccountId", schema: "treasury", table: "SettlementApplications", columns: new[] { "AccountId" });
        migrationBuilder.Sql("""
            EXEC(N'CREATE TRIGGER [treasury].[TR_SettlementApplications_Immutable] ON [treasury].[SettlementApplications] AFTER UPDATE, DELETE AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM deleted) THROW 51024, ''Accounting audit payload is immutable; preserve history and use a linked reversal.'', 1;
            END;');
            """);
        migrationBuilder.CreateTable(name: "CreditAudit", schema: "sales", columns: table => new
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
        }, constraints: table =>
        {
            table.PrimaryKey("PK_CreditAudit", x => x.Id);
            table.ForeignKey("FK_CreditAudit_Companies_CompanyId", x => x.CompanyId, "Companies", "Id", "system", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_CreditAudit_Branches_BranchId", x => x.BranchId, "Branches", "Id", "system", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_CreditAudit_Invoices_InvoiceId", x => x.InvoiceId, "Invoices", "Id", "sales", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_CreditAudit_Parties_PartyId", x => x.PartyId, "Parties", "Id", "parties", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_CreditAudit_Users_ActorUserId", x => x.ActorUserId, "Users", "Id", "security", onDelete: ReferentialAction.Restrict);
            table.CheckConstraint("CK_SalesCreditAudit_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
            table.CheckConstraint("CK_SalesCreditAudit_Amounts", "[CreditLimitBase] >= 0 AND [InvoiceBaseAmount] >= 0 AND [ExposureAfterBase] = [ExposureBeforeBase] + [InvoiceBaseAmount]");
            table.CheckConstraint("CK_SalesCreditAudit_Policy", "[PolicyKind] IN (1,2,3) AND ([WasCreditBlocked] = 0 OR [IsOverride] = 1)");
            table.CheckConstraint("CK_SalesCreditAudit_Reason", "LEN(LTRIM(RTRIM([Reason]))) > 0");
            table.CheckConstraint("CK_SalesCreditAudit_Utc", "DATEPART(TZOFFSET,[RecordedAtUtc]) = 0");
        });
        migrationBuilder.CreateIndex(name: "IX_CreditAudit_CompanyId_BranchId_InvoiceId", schema: "sales", table: "CreditAudit", columns: new[] { "CompanyId", "BranchId", "InvoiceId" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_CreditAudit_BranchId", schema: "sales", table: "CreditAudit", columns: new[] { "BranchId" });
        migrationBuilder.CreateIndex(name: "IX_CreditAudit_InvoiceId", schema: "sales", table: "CreditAudit", columns: new[] { "InvoiceId" });
        migrationBuilder.CreateIndex(name: "IX_CreditAudit_PartyId", schema: "sales", table: "CreditAudit", columns: new[] { "PartyId" });
        migrationBuilder.CreateIndex(name: "IX_CreditAudit_ActorUserId", schema: "sales", table: "CreditAudit", columns: new[] { "ActorUserId" });
        migrationBuilder.Sql("""
            EXEC(N'CREATE TRIGGER [sales].[TR_CreditAudit_Immutable] ON [sales].[CreditAudit] AFTER UPDATE, DELETE AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM deleted) THROW 51024, ''Accounting audit payload is immutable; preserve history and use a linked reversal.'', 1;
            END;');
            """);
        migrationBuilder.CreateTable(name: "PolicyAudit", schema: "parties", columns: table => new
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
        }, constraints: table =>
        {
            table.PrimaryKey("PK_PolicyAudit", x => x.Id);
            table.ForeignKey("FK_PolicyAudit_Companies_CompanyId", x => x.CompanyId, "Companies", "Id", "system", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_PolicyAudit_Parties_PartyId", x => x.PartyId, "Parties", "Id", "parties", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_PolicyAudit_Users_ActorId", x => x.ActorId, "Users", "Id", "security", onDelete: ReferentialAction.Restrict);
            table.CheckConstraint("CK_PartyPolicyAudit_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
            table.CheckConstraint("CK_PartyPolicyAudit_Limits", "[BeforeCreditLimit]>=0 AND [AfterCreditLimit]>=0");
            table.CheckConstraint("CK_PartyPolicyAudit_Policies", "[BeforePolicy] IN (1,2,3) AND [AfterPolicy] IN (1,2,3)");
            table.CheckConstraint("CK_PartyPolicyAudit_Utc", "DATEPART(TZOFFSET,[DateUtc])=0");
        });
        migrationBuilder.CreateIndex(name: "IX_PolicyAudit_CompanyId_PartyId_DateUtc", schema: "parties", table: "PolicyAudit", columns: new[] { "CompanyId", "PartyId", "DateUtc" });
        migrationBuilder.CreateIndex(name: "IX_PolicyAudit_PartyId", schema: "parties", table: "PolicyAudit", columns: new[] { "PartyId" });
        migrationBuilder.CreateIndex(name: "IX_PolicyAudit_ActorId", schema: "parties", table: "PolicyAudit", columns: new[] { "ActorId" });
        migrationBuilder.Sql("""
            EXEC(N'CREATE TRIGGER [parties].[TR_PolicyAudit_Immutable] ON [parties].[PolicyAudit] AFTER UPDATE, DELETE AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM deleted) THROW 51024, ''Accounting audit payload is immutable; preserve history and use a linked reversal.'', 1;
            END;');
            """);
        migrationBuilder.InsertData(schema: "security", table: "Permissions", column: "Code", value: "Sales.Credit.Override");
        migrationBuilder.InsertData(schema: "security", table: "Permissions", column: "Code", value: "Treasury.Vouchers.Approve");
        migrationBuilder.InsertData(schema: "security", table: "Permissions", column: "Code", value: "Treasury.Vouchers.Allocate");
        migrationBuilder.Sql("""
            INSERT INTO [security].[RolePermissions] ([RoleId],[PermissionCode],[CreatedAtUtc])
            SELECT r.[Id],p.[Code],SYSUTCDATETIME() FROM [security].[Roles] r CROSS JOIN [security].[Permissions] p
            WHERE r.[Code]=N'ADMINISTRATOR' AND p.[Code] IN (N'Sales.Credit.Override', N'Treasury.Vouchers.Approve', N'Treasury.Vouchers.Allocate')
            AND NOT EXISTS (SELECT 1 FROM [security].[RolePermissions] rp WHERE rp.[RoleId]=r.[Id] AND rp.[PermissionCode]=p.[Code]);
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Refuse destructive rollback once new accounting/audit data exists. Use a reviewed recovery plan instead.
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [treasury].[AllocationPlans]) OR EXISTS (SELECT 1 FROM [treasury].[SettlementBatches])
                OR EXISTS (SELECT 1 FROM [treasury].[SettlementApplications]) OR EXISTS (SELECT 1 FROM [sales].[CreditAudit])
                OR EXISTS (SELECT 1 FROM [parties].[PolicyAudit]) OR EXISTS (SELECT 1 FROM [treasury].[Vouchers] WHERE [Status]=1)
                OR EXISTS (SELECT 1 FROM [parties].[Parties] WHERE [IsCashCustomer]=1 OR [IsCreditBlocked]=1)
                THROW 51025, 'New accounting or audit data exists; automatic rollback would lose history. Use a reviewed recovery plan.', 1;
            """);
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS [treasury].[TR_SettlementApplications_Immutable];");
        migrationBuilder.DropTable(name: "SettlementApplications", schema: "treasury");
        migrationBuilder.DropTable(name: "SettlementBatches", schema: "treasury");
        migrationBuilder.DropTable(name: "AllocationPlans", schema: "treasury");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS [sales].[TR_CreditAudit_Immutable];");
        migrationBuilder.DropTable(name: "CreditAudit", schema: "sales");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS [parties].[TR_PolicyAudit_Immutable];");
        migrationBuilder.DropTable(name: "PolicyAudit", schema: "parties");
        migrationBuilder.DropForeignKey(name: "FK_Vouchers_Accounts_PartyControlAccountId", schema: "treasury", table: "Vouchers");
        migrationBuilder.DropIndex(name: "IX_Vouchers_PartyControlAccountId", schema: "treasury", table: "Vouchers");
        migrationBuilder.DropForeignKey(name: "FK_Vouchers_Users_ApprovedBy", schema: "treasury", table: "Vouchers");
        migrationBuilder.DropIndex(name: "IX_Vouchers_ApprovedBy", schema: "treasury", table: "Vouchers");
        migrationBuilder.DropIndex(name: "UQ_Parties_CashCustomer", schema: "parties", table: "Parties");
        migrationBuilder.DropColumn(name: "IsCashCustomer", schema: "parties", table: "Parties");
        migrationBuilder.DropColumn(name: "IsCreditBlocked", schema: "parties", table: "Parties");
        migrationBuilder.DropColumn(name: "ApprovedBy", schema: "treasury", table: "Vouchers");
        migrationBuilder.DropColumn(name: "ApprovedAtUtc", schema: "treasury", table: "Vouchers");
        migrationBuilder.DropColumn(name: "ApprovalReason", schema: "treasury", table: "Vouchers");
        migrationBuilder.DropColumn(name: "PartyControlAccountId", schema: "treasury", table: "Vouchers");
        migrationBuilder.DropCheckConstraint(name: "CK_Vouchers_Status", schema: "treasury", table: "Vouchers");
        migrationBuilder.AddCheckConstraint(name: "CK_Vouchers_Status", schema: "treasury", table: "Vouchers", sql: "[Status] IN (0, 2, 3)");
        migrationBuilder.DropCheckConstraint(name: "CK_Vouchers_Posted", schema: "treasury", table: "Vouchers");
        migrationBuilder.AddCheckConstraint(name: "CK_Vouchers_Posted", schema: "treasury", table: "Vouchers", sql: "[Status] = 0 OR ([JournalEntryId] IS NOT NULL AND [VoucherNumber] IS NOT NULL AND [PostingKey] IS NOT NULL AND [PostedAtUtc] IS NOT NULL)");
        migrationBuilder.Sql("DELETE FROM [security].[RolePermissions] WHERE [PermissionCode] IN (N'Sales.Credit.Override', N'Treasury.Vouchers.Approve', N'Treasury.Vouchers.Allocate');");
        migrationBuilder.DeleteData(schema: "security", table: "Permissions", keyColumn: "Code", keyValue: "Sales.Credit.Override");
        migrationBuilder.DeleteData(schema: "security", table: "Permissions", keyColumn: "Code", keyValue: "Treasury.Vouchers.Approve");
        migrationBuilder.DeleteData(schema: "security", table: "Permissions", keyColumn: "Code", keyValue: "Treasury.Vouchers.Allocate");
    }
}
