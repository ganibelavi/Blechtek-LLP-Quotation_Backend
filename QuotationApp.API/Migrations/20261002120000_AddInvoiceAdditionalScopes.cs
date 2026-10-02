using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuotationApp.API.Migrations;

[Migration("20261002120000_AddInvoiceAdditionalScopes")]
public partial class AddInvoiceAdditionalScopes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "additional_scopes_json",
            table: "invoices",
            type: "nvarchar(max)",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "additional_scopes_json",
            table: "invoices");
    }
}
