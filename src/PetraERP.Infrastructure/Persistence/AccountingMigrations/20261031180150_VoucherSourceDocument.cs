using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace PetraERP.Infrastructure.Persistence.AccountingMigrations;

/// <inheritdoc />
public partial class VoucherSourceDocument : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateOnly>(
            name: "SourceDate",
            schema: "treasury",
            table: "Vouchers",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SourceNumber",
            schema: "treasury",
            table: "Vouchers",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "SourceDate",
            schema: "treasury",
            table: "Vouchers");

        migrationBuilder.DropColumn(
            name: "SourceNumber",
            schema: "treasury",
            table: "Vouchers");
    }
}
