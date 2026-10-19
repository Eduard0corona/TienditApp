using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerCredit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SoldOnCredit",
                table: "Orders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "CreditLimit",
                table: "Customers",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TenantId_CustomerPhone",
                table: "Orders",
                columns: new[] { "TenantId", "CustomerPhone" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A limit or a sale on credit is the business's word on who owes it what: rolling back would forget it, so once a
            // business sells on credit there is only roll-forward. The check runs as dynamic SQL: a second run of
            // rollback.sql, with the columns gone, skips it without compiling it (a missing column, unlike a missing
            // table, would not compile).
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [Customers] WHERE [CreditLimit] IS NOT NULL) OR EXISTS (SELECT 1 FROM [Orders] WHERE [SoldOnCredit] = 1)
    THROW 50026, N''Businesses sell on credit already; rolling back would forget who may owe and what was sold on credit. Roll forward instead.'', 1;');
");

            migrationBuilder.DropIndex(
                name: "IX_Orders_TenantId_CustomerPhone",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "SoldOnCredit",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CreditLimit",
                table: "Customers");
        }
    }
}
