using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuotationApp.API.Migrations;

[Migration("20260928000000_AddPoVerificationFields")]
public partial class AddPoVerificationFields : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ClientPoNumber",
            table: "purchase_orders",
            type: "nvarchar(100)",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "ClientPoDate",
            table: "purchase_orders",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "ClientPoAmount",
            table: "purchase_orders",
            type: "decimal(18,2)",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ClientPoItems",
            table: "purchase_orders",
            type: "nvarchar(max)",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ClientPoTerms",
            table: "purchase_orders",
            type: "nvarchar(max)",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "UploadedFilePath",
            table: "purchase_orders",
            type: "nvarchar(500)",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "UploadedFileName",
            table: "purchase_orders",
            type: "nvarchar(255)",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "FileContentType",
            table: "purchase_orders",
            type: "nvarchar(100)",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "VerificationStatus",
            table: "purchase_orders",
            type: "nvarchar(30)",
            nullable: false,
            defaultValue: "Draft");

        migrationBuilder.AddColumn<int>(
            name: "CreatedBy",
            table: "purchase_orders",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "VerifiedBy",
            table: "purchase_orders",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "VerifiedAt",
            table: "purchase_orders",
            type: "datetime2",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "VerificationNotes",
            table: "purchase_orders",
            type: "nvarchar(max)",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "PoAuditLog",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                PoId = table.Column<int>(type: "int", nullable: false),
                Action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                ChangedBy = table.Column<int>(type: "int", nullable: false),
                ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PoAuditLog", x => x.Id);
                table.ForeignKey(
                    name: "FK_PoAuditLog_purchase_orders_PoId",
                    column: x => x.PoId,
                    principalTable: "purchase_orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PoAuditLog_PoId",
            table: "PoAuditLog",
            column: "PoId");

        // Unique index on (customer_id, ClientPoNumber) ignoring NULLs
        migrationBuilder.Sql(@"
            CREATE UNIQUE NONCLUSTERED INDEX UQ_purchase_orders_Customer_ClientPoNumber
            ON purchase_orders (customer_id, ClientPoNumber)
            WHERE ClientPoNumber IS NOT NULL;
        ");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "UQ_purchase_orders_Customer_ClientPoNumber",
            table: "purchase_orders");

        migrationBuilder.DropTable(
            name: "PoAuditLog");

        migrationBuilder.DropColumn(
            name: "ClientPoNumber",
            table: "purchase_orders");

        migrationBuilder.DropColumn(
            name: "ClientPoDate",
            table: "purchase_orders");

        migrationBuilder.DropColumn(
            name: "ClientPoAmount",
            table: "purchase_orders");

        migrationBuilder.DropColumn(
            name: "ClientPoItems",
            table: "purchase_orders");

        migrationBuilder.DropColumn(
            name: "ClientPoTerms",
            table: "purchase_orders");

        migrationBuilder.DropColumn(
            name: "UploadedFilePath",
            table: "purchase_orders");

        migrationBuilder.DropColumn(
            name: "UploadedFileName",
            table: "purchase_orders");

        migrationBuilder.DropColumn(
            name: "FileContentType",
            table: "purchase_orders");

        migrationBuilder.DropColumn(
            name: "VerificationStatus",
            table: "purchase_orders");

        migrationBuilder.DropColumn(
            name: "CreatedBy",
            table: "purchase_orders");

        migrationBuilder.DropColumn(
            name: "VerifiedBy",
            table: "purchase_orders");

        migrationBuilder.DropColumn(
            name: "VerifiedAt",
            table: "purchase_orders");

        migrationBuilder.DropColumn(
            name: "VerificationNotes",
            table: "purchase_orders");
    }
}