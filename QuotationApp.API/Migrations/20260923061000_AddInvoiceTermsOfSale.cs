using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuotationApp.API.Migrations;

public partial class AddInvoiceTermsOfSale : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "terms_of_sale",
            table: "invoices",
            type: "nvarchar(4000)",
            maxLength: 4000,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "terms_of_sale",
            table: "invoices");
    }
}