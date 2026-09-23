using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuotationApp.API.Migrations;

public partial class AddInvoiceTimeOfIssue : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "time_of_issue",
            table: "invoices",
            type: "nvarchar(10)",
            maxLength: 10,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "time_of_issue",
            table: "invoices");
    }
}