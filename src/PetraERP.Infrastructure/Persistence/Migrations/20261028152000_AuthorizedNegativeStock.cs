using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetraERP.Infrastructure.Persistence.Migrations;
    /// <inheritdoc />
    public partial class AuthorizedNegativeStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_StockMovements_AuditBalance",
                schema: "inventory",
                table: "StockMovements");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockMovements_AuditBalance",
                schema: "inventory",
                table: "StockMovements",
                sql: "([QuantityBefore] IS NULL AND [QuantityAfter] IS NULL AND [ValueBefore] IS NULL AND [ValueAfter] IS NULL) OR ([QuantityBefore] IS NOT NULL AND [QuantityAfter] IS NOT NULL AND [ValueBefore] IS NOT NULL AND [ValueAfter] IS NOT NULL AND (([QuantityBefore]>0 AND [ValueBefore]>=0) OR ([QuantityBefore]<0 AND [ValueBefore]<=0) OR ([QuantityBefore]=0 AND [ValueBefore]=0)) AND (([QuantityAfter]>0 AND [ValueAfter]>=0) OR ([QuantityAfter]<0 AND [ValueAfter]<=0) OR ([QuantityAfter]=0 AND [ValueAfter]=0)) AND [QuantityAfter]=[QuantityBefore]+[BaseQuantity] AND [ValueAfter]=[ValueBefore]+CASE WHEN [BaseQuantity]<0 THEN -[TotalCost] ELSE [TotalCost] END AND [RecordedAtUtc] IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_StockMovements_AuditBalance",
                schema: "inventory",
                table: "StockMovements");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockMovements_AuditBalance",
                schema: "inventory",
                table: "StockMovements",
                sql: "([QuantityBefore] IS NULL AND [QuantityAfter] IS NULL AND [ValueBefore] IS NULL AND [ValueAfter] IS NULL) OR ([QuantityBefore] IS NOT NULL AND [QuantityAfter] IS NOT NULL AND [ValueBefore] IS NOT NULL AND [ValueAfter] IS NOT NULL AND [QuantityBefore]>=0 AND [QuantityAfter]>=0 AND [ValueBefore]>=0 AND [ValueAfter]>=0 AND [QuantityAfter]=[QuantityBefore]+[BaseQuantity] AND [ValueAfter]=[ValueBefore]+CASE WHEN [BaseQuantity]<0 THEN -[TotalCost] ELSE [TotalCost] END AND [RecordedAtUtc] IS NOT NULL)");
        }
    }
