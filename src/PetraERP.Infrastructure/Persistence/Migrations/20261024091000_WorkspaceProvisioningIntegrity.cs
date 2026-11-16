using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace PetraERP.Infrastructure.Persistence.Migrations;

[DbContext(typeof(PetraDbContext))]
[Migration("20261024091000_WorkspaceProvisioningIntegrity")]
public sealed partial class WorkspaceProvisioningIntegrity : Migration
{
    private static readonly string[] CompanyYearCode = ["CompanyId", "YearCode"];
    private static readonly string[] CompanyBranch = ["CompanyId", "BranchId"];
    private static readonly string[] BranchIdentity = ["CompanyId", "Id"];
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        // Do not choose a default or invent historical ranges during an upgrade.
        migrationBuilder.Sql("""
            IF EXISTS (SELECT [CompanyId] FROM [system].[FiscalDatabases]
                       WHERE [IsDefault] = 1 GROUP BY [CompanyId] HAVING COUNT(*) > 1)
                THROW 51011, N'Multiple default fiscal years exist. Select the correct default before upgrading.', 1;
            """);
        migrationBuilder.AddColumn<string>("YearCode", "FiscalDatabases", "nvarchar(64)", schema: "system", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<DateOnly>("RangeStart", "FiscalDatabases", "date", schema: "system", nullable: true);
        migrationBuilder.AddColumn<DateOnly>("RangeEnd", "FiscalDatabases", "date", schema: "system", nullable: true);
        migrationBuilder.AddCheckConstraint("CK_FiscalDatabases_Range", "FiscalDatabases",
            "([RangeStart] IS NULL AND [RangeEnd] IS NULL) OR ([RangeStart] IS NOT NULL AND [RangeEnd] IS NOT NULL AND [RangeStart] <= [RangeEnd])", "system");
        migrationBuilder.CreateIndex("UX_FiscalDatabases_CompanyDefault", "FiscalDatabases", "CompanyId", "system", unique: true, filter: "[IsDefault] = 1");
        migrationBuilder.CreateIndex("IX_FiscalDatabases_CompanyId_YearCode", "FiscalDatabases", CompanyYearCode, "system", unique: true, filter: "[YearCode] IS NOT NULL");
        migrationBuilder.DropForeignKey("FK_Warehouses_Branches_BranchId", "Warehouses", "inventory");
        migrationBuilder.DropIndex("IX_Warehouses_BranchId", "Warehouses", "inventory");
        migrationBuilder.AddForeignKey("FK_Warehouses_Branches_CompanyId_BranchId", "Warehouses",
            CompanyBranch, "Branches", schema: "inventory", principalSchema: "system",
            principalColumns: BranchIdentity, onDelete: ReferentialAction.Restrict);
        migrationBuilder.DropCheckConstraint("CK_FiscalYears_Range", "FiscalYears", "ledger");
        migrationBuilder.AddCheckConstraint("CK_FiscalYears_Range", "FiscalYears", "[RangeEnd] >= [RangeStart]", "ledger");
        migrationBuilder.DropCheckConstraint("CK_AccountingPeriods_Range", "AccountingPeriods", "ledger");
        migrationBuilder.AddCheckConstraint("CK_AccountingPeriods_Range", "AccountingPeriods", "[RangeEnd] >= [RangeStart]", "ledger");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.DropCheckConstraint("CK_FiscalYears_Range", "FiscalYears", "ledger");
        migrationBuilder.AddCheckConstraint("CK_FiscalYears_Range", "FiscalYears", "[RangeEnd] > [RangeStart]", "ledger");
        migrationBuilder.DropCheckConstraint("CK_AccountingPeriods_Range", "AccountingPeriods", "ledger");
        migrationBuilder.AddCheckConstraint("CK_AccountingPeriods_Range", "AccountingPeriods", "[RangeEnd] > [RangeStart]", "ledger");
        migrationBuilder.DropForeignKey("FK_Warehouses_Branches_CompanyId_BranchId", "Warehouses", "inventory");
        migrationBuilder.CreateIndex("IX_Warehouses_BranchId", "Warehouses", "BranchId", "inventory");
        migrationBuilder.AddForeignKey("FK_Warehouses_Branches_BranchId", "Warehouses", "BranchId", "Branches",
            schema: "inventory", principalSchema: "system", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.DropIndex("UX_FiscalDatabases_CompanyDefault", "FiscalDatabases", "system");
        migrationBuilder.DropIndex("IX_FiscalDatabases_CompanyId_YearCode", "FiscalDatabases", "system");
        migrationBuilder.DropCheckConstraint("CK_FiscalDatabases_Range", "FiscalDatabases", "system");
        migrationBuilder.DropColumn("YearCode", "FiscalDatabases", "system");
        migrationBuilder.DropColumn("RangeStart", "FiscalDatabases", "system");
        migrationBuilder.DropColumn("RangeEnd", "FiscalDatabases", "system");
    }
}
