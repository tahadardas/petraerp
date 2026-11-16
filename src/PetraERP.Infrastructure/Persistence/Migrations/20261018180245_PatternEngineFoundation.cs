using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace PetraERP.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class PatternEngineFoundation : Migration
{
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "patterns");

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
                        name: "FK_PatternRevisions_DocumentPatterns",
                        column: x => x.PatternId,
                        principalSchema: "patterns",
                        principalTable: "DocumentPatterns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PatternRevisions_Users",
                        column: x => x.CreatedByUserId,
                        principalSchema: "security",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "security",
                table: "Permissions",
                column: "Code",
                values: new object[]
                {
                    "Patterns.Documents.Manage",
                    "Patterns.Documents.Publish",
                    "Patterns.Documents.View"
                });

            // Extend the built-in installation administrator only; other roles require explicit grants.
            migrationBuilder.Sql("""
                INSERT INTO [security].[RolePermissions] ([RoleId], [PermissionCode], [CreatedAtUtc])
                SELECT r.[Id], p.[Code], SYSUTCDATETIME()
                FROM [security].[Roles] r CROSS JOIN [security].[Permissions] p
                WHERE r.[Code] = N'ADMINISTRATOR' AND p.[Code] IN (N'Patterns.Documents.Manage', N'Patterns.Documents.Publish', N'Patterns.Documents.View')
                AND NOT EXISTS (SELECT 1 FROM [security].[RolePermissions] rp WHERE rp.[RoleId] = r.[Id] AND rp.[PermissionCode] = p.[Code]);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentPatterns_CompanyActive",
                schema: "patterns",
                table: "DocumentPatterns",
                columns: new[] { "CompanyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "UQ_DocumentPatterns_CompanyOperationCode",
                schema: "patterns",
                table: "DocumentPatterns",
                columns: new[] { "CompanyId", "OperationKind", "Code" },
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
                columns: new[] { "PatternId", "BranchId" },
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
                columns: new[] { "PatternId", "RevisionNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM [security].[RolePermissions] WHERE [PermissionCode] IN (N'Patterns.Documents.Manage', N'Patterns.Documents.Publish', N'Patterns.Documents.View');");

            migrationBuilder.DropTable(
                name: "PatternBranchGrants",
                schema: "patterns");

            migrationBuilder.DropTable(
                name: "PatternNumberSequences",
                schema: "patterns");

            migrationBuilder.DropTable(
                name: "PatternRevisions",
                schema: "patterns");

            migrationBuilder.DropTable(
                name: "DocumentPatterns",
                schema: "patterns");

            migrationBuilder.DeleteData(
                schema: "security",
                table: "Permissions",
                keyColumn: "Code",
                keyValue: "Patterns.Documents.Manage");

            migrationBuilder.DeleteData(
                schema: "security",
                table: "Permissions",
                keyColumn: "Code",
                keyValue: "Patterns.Documents.Publish");

            migrationBuilder.DeleteData(
                schema: "security",
                table: "Permissions",
                keyColumn: "Code",
                keyValue: "Patterns.Documents.View");
        }
    }
