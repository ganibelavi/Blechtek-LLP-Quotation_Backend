using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using QuotationApp.API.Data;

#nullable disable

namespace QuotationApp.API.Migrations;

[DbContext(typeof(QuotationDbContext))]
[Migration("20261002174500_AddClientPoAdditionalScopes")]
public partial class AddClientPoAdditionalScopes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "client_po_additional_scopes",
            table: "purchase_orders",
            type: "nvarchar(max)",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "client_po_additional_scopes",
            table: "purchase_orders");
    }
}
