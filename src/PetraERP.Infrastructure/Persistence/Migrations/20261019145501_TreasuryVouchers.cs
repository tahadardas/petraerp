using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace PetraERP.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class TreasuryVouchers : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "treasury");

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
                table.CheckConstraint("CK_Vouchers_Posted", "[Status] = 0 OR ([JournalEntryId] IS NOT NULL AND [VoucherNumber] IS NOT NULL AND [PostingKey] IS NOT NULL AND [PostedAtUtc] IS NOT NULL)");
                table.CheckConstraint("CK_Vouchers_ReceiptParty", "[Kind] <> 1 OR [PartyId] IS NOT NULL");
                table.CheckConstraint("CK_Vouchers_Reversed", "([Status] = 3 AND [ReversalJournalEntryId] IS NOT NULL AND [ReversedAtUtc] IS NOT NULL AND [ReversalReason] IS NOT NULL) OR ([Status] <> 3 AND [ReversalJournalEntryId] IS NULL AND [ReversedAtUtc] IS NULL AND [ReversalReason] IS NULL)");
                table.CheckConstraint("CK_Vouchers_SettlementMethod", "[SettlementMethod] IN (1, 2)");
                table.CheckConstraint("CK_Vouchers_Status", "[Status] IN (0, 2, 3)");
                table.CheckConstraint("CK_Vouchers_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                table.CheckConstraint("CK_Vouchers_VoucherDateUtc", "DATEPART(TZOFFSET, [VoucherDateUtc]) = 0");
                table.ForeignKey(
                    name: "FK_Vouchers_Branches_BranchId",
                    column: x => x.BranchId,
                    principalSchema: "system",
                    principalTable: "Branches",
                    principalColumn: "Id",
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

        migrationBuilder.InsertData(
            schema: "security",
            table: "Permissions",
            column: "Code",
            values: new object[]
            {
                "Treasury.Vouchers.Manage",
                "Treasury.Vouchers.Post",
                "Treasury.Vouchers.Reverse",
                "Treasury.Vouchers.View"
            });

        migrationBuilder.CreateIndex(
            name: "IX_VoucherAllocations_SalesInvoiceId",
            schema: "treasury",
            table: "VoucherAllocations",
            column: "SalesInvoiceId");

        migrationBuilder.CreateIndex(
            name: "UQ_VoucherAllocations_Invoice",
            schema: "treasury",
            table: "VoucherAllocations",
            columns: new[] { "VoucherId", "SalesInvoiceId" },
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
            name: "IX_Vouchers_BranchId",
            schema: "treasury",
            table: "Vouchers",
            column: "BranchId");

        migrationBuilder.CreateIndex(
            name: "IX_Vouchers_Company_Party",
            schema: "treasury",
            table: "Vouchers",
            columns: new[] { "CompanyId", "PartyId" });

        migrationBuilder.CreateIndex(
            name: "IX_Vouchers_JournalEntryId",
            schema: "treasury",
            table: "Vouchers",
            column: "JournalEntryId",
            unique: true,
            filter: "[JournalEntryId] IS NOT NULL");

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
            columns: new[] { "CompanyId", "BranchId", "Kind", "VoucherDateUtc" });

        migrationBuilder.CreateIndex(
            name: "UQ_Vouchers_Number",
            schema: "treasury",
            table: "Vouchers",
            columns: new[] { "CompanyId", "BranchId", "VoucherNumber" },
            unique: true,
            filter: "[VoucherNumber] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "UQ_Vouchers_PostingKey",
            schema: "treasury",
            table: "Vouchers",
            columns: new[] { "CompanyId", "BranchId", "PostingKey" },
            unique: true,
            filter: "[PostingKey] IS NOT NULL");

        // Seeded permissions are granted to the administrator role; other roles receive them explicitly.
        migrationBuilder.Sql("""
            INSERT INTO [security].[RolePermissions] ([RoleId], [PermissionCode], [CreatedAtUtc])
            SELECT r.[Id], p.[Code], SYSUTCDATETIME()
            FROM [security].[Roles] r CROSS JOIN [security].[Permissions] p
            WHERE r.[Code] = N'ADMINISTRATOR' AND p.[Code] LIKE N'Treasury.%'
            AND NOT EXISTS (SELECT 1 FROM [security].[RolePermissions] rp WHERE rp.[RoleId] = r.[Id] AND rp.[PermissionCode] = p.[Code]);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM [security].[RolePermissions]
            WHERE [PermissionCode] IN (N'Treasury.Vouchers.Manage', N'Treasury.Vouchers.Post',
                N'Treasury.Vouchers.Reverse', N'Treasury.Vouchers.View');
            """);
        migrationBuilder.DropTable(
            name: "VoucherAllocations",
            schema: "treasury");

        migrationBuilder.DropTable(
            name: "VoucherLines",
            schema: "treasury");

        migrationBuilder.DropTable(
            name: "Vouchers",
            schema: "treasury");

        migrationBuilder.DeleteData(
            schema: "security",
            table: "Permissions",
            keyColumn: "Code",
            keyValue: "Treasury.Vouchers.Manage");

        migrationBuilder.DeleteData(
            schema: "security",
            table: "Permissions",
            keyColumn: "Code",
            keyValue: "Treasury.Vouchers.Post");

        migrationBuilder.DeleteData(
            schema: "security",
            table: "Permissions",
            keyColumn: "Code",
            keyValue: "Treasury.Vouchers.Reverse");

        migrationBuilder.DeleteData(
            schema: "security",
            table: "Permissions",
            keyColumn: "Code",
            keyValue: "Treasury.Vouchers.View");
    }
}
