using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Drops Products.StockQuantity and LowStockThreshold and CustomerProfiles.LoyaltyPoints (with its default) and
    /// LoyaltyTier: the model stopped mapping them in StopMappingStockAndLoyaltyColumns, so the version running while
    /// this is applied (the one with that migration) no longer reads them. Written by hand, because the model has no
    /// such columns. Down creates them again, empty (NULL, and 0 points by default): the version before that migration
    /// reads that as no stock captured and no points, so it can run again after it.
    /// </summary>
    public partial class DropStockAndLoyaltyColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LowStockThreshold",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "StockQuantity",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "LoyaltyPoints",
                table: "CustomerProfiles");

            migrationBuilder.DropColumn(
                name: "LoyaltyTier",
                table: "CustomerProfiles");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LowStockThreshold",
                table: "Products",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StockQuantity",
                table: "Products",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LoyaltyPoints",
                table: "CustomerProfiles",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "LoyaltyTier",
                table: "CustomerProfiles",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);
        }
    }
}
