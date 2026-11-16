using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861
#pragma warning disable IDE0161

namespace PetraERP.Infrastructure.Persistence.Migrations;

    /// <inheritdoc />
    public partial class AmeenMigrationRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MigrationRuns",
                schema: "system",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FiscalYearId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Mode = table.Column<int>(type: "int", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    CutoffDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SourceFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SourceDatabaseName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    TargetDatabaseName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    IdentityIssueCount = table.Column<int>(type: "int", nullable: false),
                    ReconciliationPassed = table.Column<bool>(type: "bit", nullable: true),
                    ReconciliationJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ReconciledAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationRuns", x => x.Id);
                    table.CheckConstraint("CK_MigrationRuns_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_MigrationRuns_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_MigrationRuns_Issues", "[IdentityIssueCount] >= 0");
                    table.CheckConstraint("CK_MigrationRuns_Mode", "[Mode] IN (1, 2)");
                    table.CheckConstraint("CK_MigrationRuns_State", "[State] BETWEEN 1 AND 11");
                    table.CheckConstraint("CK_MigrationRuns_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_MigrationRuns_Company",
                schema: "system",
                table: "MigrationRuns",
                column: "CompanyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MigrationRuns",
                schema: "system");
        }
    }
