using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuotationApp.API.Migrations
{
    /// <inheritdoc />
    public partial class InitialQuotationTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOTE: "Modules" table already exists in the database (created separately
            // for module master data), so we do NOT create it here — only reference it
            // via foreign key below.

            migrationBuilder.CreateTable(
                name: "Quotations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    OrganizationName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ValidationDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    QuotationToName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    QuotationToAddress = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    QuotationToContactNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    QuotationToEmail = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Quotations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QuotationModules",
                columns: table => new
                {
                    QuotationId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ModuleName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuotationModules", x => new { x.QuotationId, x.ModuleName });
                    table.ForeignKey(
                        name: "FK_QuotationModules_Modules_ModuleName",
                        column: x => x.ModuleName,
                        principalTable: "Modules",
                        principalColumn: "ModuleName",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuotationModules_Quotations_QuotationId",
                        column: x => x.QuotationId,
                        principalTable: "Quotations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuotationModules_ModuleName",
                table: "QuotationModules",
                column: "ModuleName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuotationModules");

            migrationBuilder.DropTable(
                name: "Quotations");

            // "Modules" is not dropped since this migration never created it.
        }
    }
}