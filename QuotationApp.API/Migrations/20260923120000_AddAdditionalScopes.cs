using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuotationApp.API.Migrations;

[Migration("20260923120000_AddAdditionalScopes")]
public partial class AddAdditionalScopes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AdditionalScopes",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                QuotationId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                Requirement = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                ModulesId = table.Column<int>(type: "int", nullable: false),
                Modules = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                NoOfManpower = table.Column<int>(type: "int", nullable: false),
                NoOfDays = table.Column<int>(type: "int", nullable: false),
                Rate = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                Price = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AdditionalScopes", x => x.Id);
                table.ForeignKey(
                    name: "FK_AdditionalScopes_Modules_ModulesId",
                    column: x => x.ModulesId,
                    principalTable: "Modules",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AdditionalScopes_Quotations_QuotationId",
                    column: x => x.QuotationId,
                    principalTable: "Quotations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AdditionalScopes_ModulesId",
            table: "AdditionalScopes",
            column: "ModulesId");

        migrationBuilder.CreateIndex(
            name: "IX_AdditionalScopes_QuotationId",
            table: "AdditionalScopes",
            column: "QuotationId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AdditionalScopes");
    }
}