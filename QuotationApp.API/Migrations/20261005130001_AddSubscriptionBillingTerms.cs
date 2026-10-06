using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuotationApp.API.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionBillingTerms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoRenew",
                table: "CustomerModuleSubscription",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "BillingCycle",
                table: "CustomerModuleSubscription",
                type: "nvarchar(15)",
                maxLength: 15,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RenewalReminderDays",
                table: "CustomerModuleSubscription",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RenewalTermMonths",
                table: "CustomerModuleSubscription",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoRenew",
                table: "CustomerModuleSubscription");

            migrationBuilder.DropColumn(
                name: "BillingCycle",
                table: "CustomerModuleSubscription");

            migrationBuilder.DropColumn(
                name: "RenewalReminderDays",
                table: "CustomerModuleSubscription");

            migrationBuilder.DropColumn(
                name: "RenewalTermMonths",
                table: "CustomerModuleSubscription");
        }
    }
}
