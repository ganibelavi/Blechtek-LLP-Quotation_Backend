using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuotationApp.API.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesOrderSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "sales_order_id",
                table: "invoices",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SalesOrderId",
                table: "CustomerModuleSubscription",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SalesOrderNumberSeries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FinancialYear = table.Column<string>(type: "nvarchar(9)", maxLength: 9, nullable: false),
                    Prefix = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false, defaultValue: "SO"),
                    LastNumber = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrderNumberSeries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SalesOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SoNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SoDate = table.Column<DateTime>(type: "date", nullable: false),
                    QuotationId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PurchaseOrderId = table.Column<int>(type: "int", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    CustomerPoNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CustomerPoDate = table.Column<DateTime>(type: "date", nullable: true),
                    BillingAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ShippingAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CustomerGstin = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    PlaceOfSupplyState = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsInterState = table.Column<bool>(type: "bit", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false, defaultValue: "INR"),
                    SubTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DiscountTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TaxableAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CgstAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SgstAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IgstAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RoundOff = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    GrandTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    InvoicedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PaymentTermsDays = table.Column<int>(type: "int", nullable: true),
                    PaymentTermsText = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    BillingType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "OneTime"),
                    BankAccountId = table.Column<int>(type: "int", nullable: true),
                    TermsTemplateId = table.Column<int>(type: "int", nullable: true),
                    TermsAndConditions = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsSubscription = table.Column<bool>(type: "bit", nullable: false),
                    SubscriptionStart = table.Column<DateTime>(type: "date", nullable: true),
                    SubscriptionEnd = table.Column<DateTime>(type: "date", nullable: true),
                    BillingCycle = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    RenewalTermMonths = table.Column<int>(type: "int", nullable: true),
                    AutoRenew = table.Column<bool>(type: "bit", nullable: false),
                    RenewalReminderDays = table.Column<int>(type: "int", nullable: true),
                    HasMismatch = table.Column<bool>(type: "bit", nullable: false),
                    MismatchRemarks = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    VerifiedBy = table.Column<int>(type: "int", nullable: true),
                    VerifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VerificationRemarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false, defaultValue: "Draft"),
                    CancelReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    InternalRemarks = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVer = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesOrders_Quotations_QuotationId",
                        column: x => x.QuotationId,
                        principalTable: "Quotations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrders_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrders_Users_VerifiedBy",
                        column: x => x.VerifiedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrders_company_bank_accounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "company_bank_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrders_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrders_purchase_orders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "purchase_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrders_terms_templates_TermsTemplateId",
                        column: x => x.TermsTemplateId,
                        principalTable: "terms_templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesOrderBillingSchedule",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SalesOrderId = table.Column<int>(type: "int", nullable: false),
                    SequenceNo = table.Column<int>(type: "int", nullable: false),
                    MilestoneName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Percentage = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DueDate = table.Column<DateTime>(type: "date", nullable: true),
                    InvoiceId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false, defaultValue: "Pending")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrderBillingSchedule", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesOrderBillingSchedule_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SalesOrderBillingSchedule_invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesOrderDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SalesOrderId = table.Column<int>(type: "int", nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    UploadedBy = table.Column<int>(type: "int", nullable: false),
                    UploadedOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrderDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesOrderDocuments_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SalesOrderDocuments_Users_UploadedBy",
                        column: x => x.UploadedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesOrderItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SalesOrderId = table.Column<int>(type: "int", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ModuleId = table.Column<int>(type: "int", nullable: true),
                    ItemDescription = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    HsnSacCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Uom = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    QuotedUnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    PoUnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TaxableAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    GstRateId = table.Column<int>(type: "int", nullable: true),
                    GstPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    CgstAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SgstAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IgstAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    QtyInvoiced = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrderItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesOrderItems_Modules_ModuleId",
                        column: x => x.ModuleId,
                        principalTable: "Modules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrderItems_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SalesOrderItems_gst_rates_GstRateId",
                        column: x => x.GstRateId,
                        principalTable: "gst_rates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesOrderStatusHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SalesOrderId = table.Column<int>(type: "int", nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: true),
                    ToStatus = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ChangedBy = table.Column<int>(type: "int", nullable: false),
                    ChangedOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrderStatusHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesOrderStatusHistory_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SalesOrderStatusHistory_Users_ChangedBy",
                        column: x => x.ChangedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_invoices_sales_order_id",
                table: "invoices",
                column: "sales_order_id");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerModuleSubscription_SalesOrderId",
                table: "CustomerModuleSubscription",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderBillingSchedule_InvoiceId",
                table: "SalesOrderBillingSchedule",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderBillingSchedule_SalesOrderId_SequenceNo",
                table: "SalesOrderBillingSchedule",
                columns: new[] { "SalesOrderId", "SequenceNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderDocuments_SalesOrderId",
                table: "SalesOrderDocuments",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderDocuments_UploadedBy",
                table: "SalesOrderDocuments",
                column: "UploadedBy");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderItems_GstRateId",
                table: "SalesOrderItems",
                column: "GstRateId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderItems_ModuleId",
                table: "SalesOrderItems",
                column: "ModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderItems_SalesOrderId",
                table: "SalesOrderItems",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderItems_SalesOrderId_LineNo",
                table: "SalesOrderItems",
                columns: new[] { "SalesOrderId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderNumberSeries_FinancialYear",
                table: "SalesOrderNumberSeries",
                column: "FinancialYear",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_BankAccountId",
                table: "SalesOrders",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_CreatedBy",
                table: "SalesOrders",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_CustomerId_Status",
                table: "SalesOrders",
                columns: new[] { "CustomerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_IsDeleted",
                table: "SalesOrders",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_PurchaseOrderId",
                table: "SalesOrders",
                column: "PurchaseOrderId",
                unique: true,
                filter: "[Status] <> 'Cancelled' AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_QuotationId",
                table: "SalesOrders",
                column: "QuotationId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_SoDate",
                table: "SalesOrders",
                column: "SoDate");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_SoNumber",
                table: "SalesOrders",
                column: "SoNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_TermsTemplateId",
                table: "SalesOrders",
                column: "TermsTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_VerifiedBy",
                table: "SalesOrders",
                column: "VerifiedBy");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderStatusHistory_ChangedBy",
                table: "SalesOrderStatusHistory",
                column: "ChangedBy");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderStatusHistory_SalesOrderId_ChangedOn",
                table: "SalesOrderStatusHistory",
                columns: new[] { "SalesOrderId", "ChangedOn" });

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerModuleSubscription_SalesOrders_SalesOrderId",
                table: "CustomerModuleSubscription",
                column: "SalesOrderId",
                principalTable: "SalesOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_invoices_SalesOrders_sales_order_id",
                table: "invoices",
                column: "sales_order_id",
                principalTable: "SalesOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(@"
ALTER TABLE dbo.SalesOrders ADD CONSTRAINT CK_SalesOrders_Status
    CHECK (Status IN ('Draft','PendingVerification','Confirmed','PartiallyInvoiced','Invoiced','OnHold','Cancelled'));
ALTER TABLE dbo.SalesOrders ADD CONSTRAINT CK_SalesOrders_BillingType
    CHECK (BillingType IN ('OneTime','Advance','Milestone','Recurring'));
ALTER TABLE dbo.SalesOrderItems ADD CONSTRAINT CK_SalesOrderItems_Quantity
    CHECK (Quantity > 0 AND QtyInvoiced >= 0 AND QtyInvoiced <= Quantity);
ALTER TABLE dbo.SalesOrderBillingSchedule ADD CONSTRAINT CK_SalesOrderBillingSchedule_Status
    CHECK (Status IN ('Pending','Invoiced','Cancelled'));");

            migrationBuilder.Sql(@"
CREATE OR ALTER PROCEDURE dbo.usp_GetNextSoNumber
    @FinancialYear VARCHAR(9),
    @SoNumber VARCHAR(30) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @n INT, @prefix VARCHAR(10);

    SELECT @n = LastNumber, @prefix = Prefix
    FROM dbo.SalesOrderNumberSeries WITH (UPDLOCK, HOLDLOCK)
    WHERE FinancialYear = @FinancialYear;

    IF @n IS NULL
    BEGIN
        INSERT dbo.SalesOrderNumberSeries (FinancialYear, Prefix, LastNumber)
        VALUES (@FinancialYear, 'SO', 0);
        SET @n = 0;
        SET @prefix = 'SO';
    END;

    UPDATE dbo.SalesOrderNumberSeries
       SET LastNumber = LastNumber + 1
     WHERE FinancialYear = @FinancialYear;

    SET @n = @n + 1;
    SET @SoNumber = CONCAT(@prefix, '/', @FinancialYear, '/', RIGHT('0000' + CAST(@n AS VARCHAR(10)), 4));
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_GetNextSoNumber;");

            migrationBuilder.DropForeignKey(
                name: "FK_CustomerModuleSubscription_SalesOrders_SalesOrderId",
                table: "CustomerModuleSubscription");

            migrationBuilder.DropForeignKey(
                name: "FK_invoices_SalesOrders_sales_order_id",
                table: "invoices");

            migrationBuilder.DropTable(
                name: "SalesOrderBillingSchedule");

            migrationBuilder.DropTable(
                name: "SalesOrderDocuments");

            migrationBuilder.DropTable(
                name: "SalesOrderItems");

            migrationBuilder.DropTable(
                name: "SalesOrderNumberSeries");

            migrationBuilder.DropTable(
                name: "SalesOrderStatusHistory");

            migrationBuilder.DropTable(
                name: "SalesOrders");

            migrationBuilder.DropIndex(
                name: "IX_invoices_sales_order_id",
                table: "invoices");

            migrationBuilder.DropIndex(
                name: "IX_CustomerModuleSubscription_SalesOrderId",
                table: "CustomerModuleSubscription");

            migrationBuilder.DropColumn(
                name: "sales_order_id",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "SalesOrderId",
                table: "CustomerModuleSubscription");
        }
    }
}
