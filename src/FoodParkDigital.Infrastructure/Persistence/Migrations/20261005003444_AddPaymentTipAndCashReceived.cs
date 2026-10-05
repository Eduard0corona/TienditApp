using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentTipAndCashReceived : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CashReceived",
                table: "OrderPayments",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Tip",
                table: "OrderPayments",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A tip is part of the sale's history (it entered the drawer as its own movement); the cash received only
            // explained the change given. EXEC: run again once rolled back, the batch must compile without [Tip].
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [OrderPayments] WHERE [Tip] <> 0)
    THROW 50006, N''There are payments with a tip; rolling back would lose the tip of each payment.'', 1;');
");

            migrationBuilder.DropColumn(
                name: "CashReceived",
                table: "OrderPayments");

            migrationBuilder.DropColumn(
                name: "Tip",
                table: "OrderPayments");
        }
    }
}
