using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuotationApp.API.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesOrderBankSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BankAccountNoSnapshot",
                table: "SalesOrders",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankAccountTypeSnapshot",
                table: "SalesOrders",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankIfscSnapshot",
                table: "SalesOrders",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankMsmeNoSnapshot",
                table: "SalesOrders",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankNameSnapshot",
                table: "SalesOrders",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BankAccountNoSnapshot",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "BankAccountTypeSnapshot",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "BankIfscSnapshot",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "BankMsmeNoSnapshot",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "BankNameSnapshot",
                table: "SalesOrders");
        }
    }
}
