using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1861

namespace PetraERP.Infrastructure.Persistence.AccountingMigrations;
    /// <inheritdoc />
    public partial class BulkPostingRequesterActor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_BulkPostingBatches_CompanyId_RequestedByUserId",
                schema: "posting",
                table: "BulkPostingBatches",
                columns: new[] { "CompanyId", "RequestedByUserId" });

            migrationBuilder.AddForeignKey(
                name: "FK_BulkPostingBatches_ActorReferences_CompanyId_RequestedByUserId",
                schema: "posting",
                table: "BulkPostingBatches",
                columns: new[] { "CompanyId", "RequestedByUserId" },
                principalSchema: "audit",
                principalTable: "ActorReferences",
                principalColumns: new[] { "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BulkPostingBatches_ActorReferences_CompanyId_RequestedByUserId",
                schema: "posting",
                table: "BulkPostingBatches");

            migrationBuilder.DropIndex(
                name: "IX_BulkPostingBatches_CompanyId_RequestedByUserId",
                schema: "posting",
                table: "BulkPostingBatches");
        }
    }
