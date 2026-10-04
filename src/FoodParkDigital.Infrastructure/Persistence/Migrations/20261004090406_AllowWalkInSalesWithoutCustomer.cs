using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowWalkInSalesWithoutCustomer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Counter sales to the general public may have no customer link. Every existing row has
            // exactly one, so the wider constraint is validated without touching data.
            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_CustomerLink",
                table: "Orders");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_CustomerLink",
                table: "Orders",
                sql: "([CustomerUserId] IS NULL OR [TemporaryCustomerId] IS NULL) AND ([CreatedByBusiness] = 1 OR [CustomerUserId] IS NOT NULL OR [TemporaryCustomerId] IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_CustomerLink",
                table: "Orders");

            // Walk-in sales recorded after Up have no customer link and are sales history: they are kept,
            // and the stricter rule is restored WITH NOCHECK so it only applies to new rows.
            migrationBuilder.Sql(
                "ALTER TABLE [Orders] WITH NOCHECK ADD CONSTRAINT [CK_Orders_CustomerLink] CHECK " +
                "((([CustomerUserId] IS NOT NULL AND [TemporaryCustomerId] IS NULL) OR ([CustomerUserId] IS NULL AND [TemporaryCustomerId] IS NOT NULL)));");
        }
    }
}
