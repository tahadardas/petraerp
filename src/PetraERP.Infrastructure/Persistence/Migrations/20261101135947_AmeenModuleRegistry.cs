using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861
#pragma warning disable IDE0161

namespace PetraERP.Infrastructure.Persistence.Migrations;

    /// <inheritdoc />
    public partial class AmeenModuleRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ModuleRegistry",
                schema: "system",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModuleId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Publisher = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Version = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ContractVersion = table.Column<int>(type: "int", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    PackageFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DataDirectory = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    DeclaredPermissions = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    Dependencies = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    LicenseRequirement = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModuleRegistry", x => x.Id);
                    table.CheckConstraint("CK_ModuleRegistry_Contract", "[ContractVersion] > 0");
                    table.CheckConstraint("CK_ModuleRegistry_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_ModuleRegistry_ModuleId", "[ModuleId] <> ''");
                    table.CheckConstraint("CK_ModuleRegistry_State", "[State] BETWEEN 1 AND 9");
                    table.CheckConstraint("CK_ModuleRegistry_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                });

            migrationBuilder.CreateIndex(
                name: "UX_ModuleRegistry_ModuleId",
                schema: "system",
                table: "ModuleRegistry",
                column: "ModuleId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ModuleRegistry",
                schema: "system");
        }
    }
