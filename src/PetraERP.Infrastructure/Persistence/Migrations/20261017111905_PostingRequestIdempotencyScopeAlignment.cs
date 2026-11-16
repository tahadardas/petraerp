using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetraERP.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class PostingRequestIdempotencyScopeAlignment : Migration
{
    private static readonly string[] PostingRequestPrimaryKeyColumns = ["CompanyId", "BranchId", "Operation", "IdempotencyKey"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropPrimaryKey(
            name: "PK_PostingRequests",
            schema: "ledger",
            table: "PostingRequests");

        migrationBuilder.AddPrimaryKey(
            name: "PK_PostingRequests",
            schema: "ledger",
            table: "PostingRequests",
            columns: PostingRequestPrimaryKeyColumns);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (
                SELECT [IdempotencyKey]
                FROM [ledger].[PostingRequests]
                GROUP BY [IdempotencyKey]
                HAVING COUNT(*) > 1
            )
            BEGIN
                THROW 51009, 'Downgrade blocked: PostingRequests contain duplicate IdempotencyKey values across different scopes or operations. Data must be reconciled before altering primary key.', 1;
            END
            """);

        migrationBuilder.DropPrimaryKey(
            name: "PK_PostingRequests",
            schema: "ledger",
            table: "PostingRequests");

        migrationBuilder.AddPrimaryKey(
            name: "PK_PostingRequests",
            schema: "ledger",
            table: "PostingRequests",
            column: "IdempotencyKey");
    }
}
