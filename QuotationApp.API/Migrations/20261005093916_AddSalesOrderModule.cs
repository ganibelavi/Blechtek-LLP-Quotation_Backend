using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuotationApp.API.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesOrderModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QuotationTimeEstimates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QuotationId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ModuleName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StageKey = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StartWeek = table.Column<byte>(type: "tinyint", nullable: false),
                    EndWeek = table.Column<byte>(type: "tinyint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuotationTimeEstimates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuotationTimeEstimates_Quotations_QuotationId",
                        column: x => x.QuotationId,
                        principalTable: "Quotations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuotationTimeEstimates_QuotationId_ModuleName_StageKey",
                table: "QuotationTimeEstimates",
                columns: new[] { "QuotationId", "ModuleName", "StageKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuotationTimeEstimates");
        }
    }
}
