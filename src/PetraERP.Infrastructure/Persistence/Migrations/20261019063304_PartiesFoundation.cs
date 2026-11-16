using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace PetraERP.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class PartiesFoundation : Migration
{
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "parties");

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

            migrationBuilder.InsertData(
                schema: "security",
                table: "Permissions",
                column: "Code",
                values: new object[]
                {
                    "Parties.Directory.Manage",
                    "Parties.Directory.View"
                });

            migrationBuilder.Sql("""
                INSERT INTO [security].[RolePermissions] ([RoleId], [PermissionCode], [CreatedAtUtc])
                SELECT r.[Id], p.[Code], SYSUTCDATETIME()
                FROM [security].[Roles] r CROSS JOIN [security].[Permissions] p
                WHERE r.[Code] = N'ADMINISTRATOR' AND p.[Code] IN (N'Parties.Directory.Manage', N'Parties.Directory.View')
                AND NOT EXISTS (SELECT 1 FROM [security].[RolePermissions] rp WHERE rp.[RoleId] = r.[Id] AND rp.[PermissionCode] = p.[Code]);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Parties_CompanyNormalizedSearchText",
                schema: "parties",
                table: "Parties",
                columns: new[] { "CompanyId", "NormalizedSearchText" });

            migrationBuilder.CreateIndex(
                name: "IX_Parties_CompanyRoleActive",
                schema: "parties",
                table: "Parties",
                columns: new[] { "CompanyId", "Role", "IsActive" });

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
                name: "UQ_Parties_CompanyCode",
                schema: "parties",
                table: "Parties",
                columns: new[] { "CompanyId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM [security].[RolePermissions] WHERE [PermissionCode] IN (N'Parties.Directory.Manage', N'Parties.Directory.View');");

            migrationBuilder.DropTable(
                name: "Parties",
                schema: "parties");

            migrationBuilder.DeleteData(
                schema: "security",
                table: "Permissions",
                keyColumn: "Code",
                keyValue: "Parties.Directory.Manage");

            migrationBuilder.DeleteData(
                schema: "security",
                table: "Permissions",
                keyColumn: "Code",
                keyValue: "Parties.Directory.View");
        }
    }
