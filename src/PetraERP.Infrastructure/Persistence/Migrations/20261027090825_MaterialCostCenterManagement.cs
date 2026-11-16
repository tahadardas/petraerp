using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1861

namespace PetraERP.Infrastructure.Persistence.Migrations;
    /// <inheritdoc />
    public partial class MaterialCostCenterManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                schema: "inventory",
                table: "ItemGroups",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "CenterType",
                schema: "ledger",
                table: "CostCenters",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)1);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                schema: "ledger",
                table: "CostCenters",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EndDate",
                schema: "ledger",
                table: "CostCenters",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentId",
                schema: "ledger",
                table: "CostCenters",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponsiblePerson",
                schema: "ledger",
                table: "CostCenters",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "StartDate",
                schema: "ledger",
                table: "CostCenters",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CostCenterAllocations",
                schema: "ledger",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceCostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetCostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Percentage = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostCenterAllocations", x => x.Id);
                    table.CheckConstraint("CK_CostCenterAllocations_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_CostCenterAllocations_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_CostCenterAllocations_Percentage", "[Percentage] > 0 AND [Percentage] <= 100");
                    table.CheckConstraint("CK_CostCenterAllocations_SourceTarget", "[SourceCostCenterId] <> [TargetCostCenterId]");
                    table.CheckConstraint("CK_CostCenterAllocations_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                    table.ForeignKey(
                        name: "FK_CostCenterAllocations_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "system",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CostCenterAllocations_CostCenters_SourceCostCenterId",
                        column: x => x.SourceCostCenterId,
                        principalSchema: "ledger",
                        principalTable: "CostCenters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CostCenterAllocations_CostCenters_TargetCostCenterId",
                        column: x => x.TargetCostCenterId,
                        principalSchema: "ledger",
                        principalTable: "CostCenters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ItemAccountMappings",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerType = table.Column<byte>(type: "tinyint", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatternId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemAccountMappings", x => x.Id);
                    table.CheckConstraint("CK_ItemAccountMappings_CompanyId", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_ItemAccountMappings_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_ItemAccountMappings_OwnerId", "[OwnerId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_ItemAccountMappings_OwnerType", "[OwnerType] IN (1, 2)");
                    table.CheckConstraint("CK_ItemAccountMappings_Role", "DATALENGTH([Role]) > 0 AND DATALENGTH([Role]) = DATALENGTH(TRIM(NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(32)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288) FROM [Role]))");
                    table.CheckConstraint("CK_ItemAccountMappings_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
                    table.ForeignKey(
                        name: "FK_ItemAccountMappings_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "ledger",
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemAccountMappings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "system",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CostCenters_CompanyParent",
                schema: "ledger",
                table: "CostCenters",
                columns: new[] { "CompanyId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_CostCenters_ParentId",
                schema: "ledger",
                table: "CostCenters",
                column: "ParentId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CostCenters_CenterType",
                schema: "ledger",
                table: "CostCenters",
                sql: "[CenterType] IN (1, 2, 3, 4)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CostCenters_ParentSelf",
                schema: "ledger",
                table: "CostCenters",
                sql: "[ParentId] IS NULL OR [ParentId] <> [Id]");

            migrationBuilder.CreateIndex(
                name: "IX_CostCenterAllocations_SourceCostCenterId",
                schema: "ledger",
                table: "CostCenterAllocations",
                column: "SourceCostCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_CostCenterAllocations_TargetCostCenterId",
                schema: "ledger",
                table: "CostCenterAllocations",
                column: "TargetCostCenterId");

            migrationBuilder.CreateIndex(
                name: "UQ_CostCenterAllocations_Source_Target",
                schema: "ledger",
                table: "CostCenterAllocations",
                columns: new[] { "CompanyId", "SourceCostCenterId", "TargetCostCenterId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemAccountMappings_Account",
                schema: "inventory",
                table: "ItemAccountMappings",
                columns: new[] { "CompanyId", "AccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_ItemAccountMappings_AccountId",
                schema: "inventory",
                table: "ItemAccountMappings",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "UQ_ItemAccountMappings_Scope",
                schema: "inventory",
                table: "ItemAccountMappings",
                columns: new[] { "CompanyId", "OwnerType", "OwnerId", "Role", "PatternId" },
                unique: true,
                filter: "[PatternId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_CostCenters_Parent",
                schema: "ledger",
                table: "CostCenters",
                column: "ParentId",
                principalSchema: "ledger",
                principalTable: "CostCenters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CostCenters_Parent",
                schema: "ledger",
                table: "CostCenters");

            migrationBuilder.DropTable(
                name: "CostCenterAllocations",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "ItemAccountMappings",
                schema: "inventory");

            migrationBuilder.DropIndex(
                name: "IX_CostCenters_CompanyParent",
                schema: "ledger",
                table: "CostCenters");

            migrationBuilder.DropIndex(
                name: "IX_CostCenters_ParentId",
                schema: "ledger",
                table: "CostCenters");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CostCenters_CenterType",
                schema: "ledger",
                table: "CostCenters");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CostCenters_ParentSelf",
                schema: "ledger",
                table: "CostCenters");

            migrationBuilder.DropColumn(
                name: "Description",
                schema: "inventory",
                table: "ItemGroups");

            migrationBuilder.DropColumn(
                name: "CenterType",
                schema: "ledger",
                table: "CostCenters");

            migrationBuilder.DropColumn(
                name: "Description",
                schema: "ledger",
                table: "CostCenters");

            migrationBuilder.DropColumn(
                name: "EndDate",
                schema: "ledger",
                table: "CostCenters");

            migrationBuilder.DropColumn(
                name: "ParentId",
                schema: "ledger",
                table: "CostCenters");

            migrationBuilder.DropColumn(
                name: "ResponsiblePerson",
                schema: "ledger",
                table: "CostCenters");

            migrationBuilder.DropColumn(
                name: "StartDate",
                schema: "ledger",
                table: "CostCenters");
        }
    }

