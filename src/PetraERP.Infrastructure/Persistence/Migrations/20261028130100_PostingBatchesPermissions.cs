using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1861

namespace PetraERP.Infrastructure.Persistence.Migrations;
    /// <inheritdoc />
    public partial class PostingBatchesPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "posting");

            migrationBuilder.CreateTable(
                name: "BulkPostingBatches",
                schema: "posting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FiscalYearId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    FilterJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BulkPostingBatches", x => x.Id);
                    table.CheckConstraint("CK_BulkPostingBatches_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_BulkPostingBatches_Key", "DATALENGTH([IdempotencyKey]) > 0 AND DATALENGTH([IdempotencyKey]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [IdempotencyKey]))");
                    table.CheckConstraint("CK_BulkPostingBatches_Reason", "DATALENGTH([Reason]) > 0 AND DATALENGTH([Reason]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Reason]))");
                    table.CheckConstraint("CK_BulkPostingBatches_Status", "[Status] IN (0,1,2,3,4,5)");
                    table.CheckConstraint("CK_BulkPostingBatches_Times", "([StartedAtUtc] IS NULL OR DATEPART(TZOFFSET,[StartedAtUtc])=0) AND ([CompletedAtUtc] IS NULL OR DATEPART(TZOFFSET,[CompletedAtUtc])=0) AND ([CompletedAtUtc] IS NULL OR [StartedAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_BulkPostingBatches_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                    table.ForeignKey(
                        name: "FK_BulkPostingBatches_Branches_BranchId",
                        column: x => x.BranchId,
                        principalSchema: "system",
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BulkPostingBatches_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "system",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BulkPostingBatches_FiscalYears_FiscalYearId",
                        column: x => x.FiscalYearId,
                        principalSchema: "ledger",
                        principalTable: "FiscalYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BulkPostingBatches_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalSchema: "security",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BulkPostingItems",
                schema: "posting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentType = table.Column<byte>(type: "tinyint", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    DocumentDateUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    PartyName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    CurrencyCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    PreviousStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    NewStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    JournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PostingNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    Classification = table.Column<byte>(type: "tinyint", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    FailureCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    ProcessingOrder = table.Column<int>(type: "int", nullable: false),
                    LastAttemptAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BulkPostingItems", x => x.Id);
                    table.CheckConstraint("CK_BulkPostingItems_Amount", "[Amount] IS NULL OR [Amount] >= 0");
                    table.CheckConstraint("CK_BulkPostingItems_Attempts", "[AttemptCount] >= 0 AND [ProcessingOrder] >= 0");
                    table.CheckConstraint("CK_BulkPostingItems_Classification", "[Classification] BETWEEN 0 AND 12");
                    table.CheckConstraint("CK_BulkPostingItems_DocumentType", "[DocumentType] BETWEEN 0 AND 4");
                    table.CheckConstraint("CK_BulkPostingItems_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_BulkPostingItems_Status", "[Status] IN (0,1,2,3,4,5)");
                    table.CheckConstraint("CK_BulkPostingItems_Utc", "DATEPART(TZOFFSET,[DocumentDateUtc])=0 AND DATEPART(TZOFFSET,[CreatedAtUtc])=0 AND DATEPART(TZOFFSET,[UpdatedAtUtc])=0");
                    table.ForeignKey(
                        name: "FK_BulkPostingItems_Branches_BranchId",
                        column: x => x.BranchId,
                        principalSchema: "system",
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BulkPostingItems_BulkPostingBatches_BatchId",
                        column: x => x.BatchId,
                        principalSchema: "posting",
                        principalTable: "BulkPostingBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BulkPostingItems_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "system",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "security",
                table: "Permissions",
                column: "Code",
                value: "Posting.Batches.Manage");

            // Grant the new permission to an already-provisioned ADMINISTRATOR role (existing databases). A fresh
            // installation grants it through the bootstrapper's full catalog; NOT EXISTS keeps this idempotent.
            migrationBuilder.Sql("""
                INSERT INTO [security].[RolePermissions] ([RoleId], [PermissionCode], [CreatedAtUtc])
                SELECT r.[Id], N'Posting.Batches.Manage', SYSUTCDATETIME()
                FROM [security].[Roles] r
                WHERE r.[Code] = N'ADMINISTRATOR'
                  AND NOT EXISTS (SELECT 1 FROM [security].[RolePermissions] rp
                                  WHERE rp.[RoleId] = r.[Id] AND rp.[PermissionCode] = N'Posting.Batches.Manage');
                """);

            migrationBuilder.CreateIndex(
                name: "IX_BulkPostingBatches_BranchId",
                schema: "posting",
                table: "BulkPostingBatches",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_BulkPostingBatches_CompanyId_BranchId_Status",
                schema: "posting",
                table: "BulkPostingBatches",
                columns: new[] { "CompanyId", "BranchId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_BulkPostingBatches_CompanyId_IdempotencyKey",
                schema: "posting",
                table: "BulkPostingBatches",
                columns: new[] { "CompanyId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BulkPostingBatches_FiscalYearId",
                schema: "posting",
                table: "BulkPostingBatches",
                column: "FiscalYearId");

            migrationBuilder.CreateIndex(
                name: "IX_BulkPostingBatches_RequestedByUserId",
                schema: "posting",
                table: "BulkPostingBatches",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BulkPostingItems_BatchId_DocumentType_DocumentId",
                schema: "posting",
                table: "BulkPostingItems",
                columns: new[] { "BatchId", "DocumentType", "DocumentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BulkPostingItems_BatchId_ProcessingOrder",
                schema: "posting",
                table: "BulkPostingItems",
                columns: new[] { "BatchId", "ProcessingOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_BulkPostingItems_BatchId_Status",
                schema: "posting",
                table: "BulkPostingItems",
                columns: new[] { "BatchId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_BulkPostingItems_BranchId",
                schema: "posting",
                table: "BulkPostingItems",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "UX_BulkPostingItems_ActiveDocument",
                schema: "posting",
                table: "BulkPostingItems",
                columns: new[] { "CompanyId", "BranchId", "DocumentType", "DocumentId" },
                unique: true,
                filter: "[Status] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BulkPostingItems",
                schema: "posting");

            migrationBuilder.DropTable(
                name: "BulkPostingBatches",
                schema: "posting");

            migrationBuilder.Sql("""
                DELETE FROM [security].[RolePermissions]
                WHERE [PermissionCode] = N'Posting.Batches.Manage';
                """);

            migrationBuilder.DeleteData(
                schema: "security",
                table: "Permissions",
                keyColumn: "Code",
                keyValue: "Posting.Batches.Manage");
        }
    }
