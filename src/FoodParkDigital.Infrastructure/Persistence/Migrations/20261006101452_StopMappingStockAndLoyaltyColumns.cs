using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The model stops mapping Products.StockQuantity and LowStockThreshold (captured by hand before stock lived in the
    /// inventory ledger) and CustomerProfiles.LoyaltyPoints and LoyaltyTier (global, never written): it changes nothing
    /// in the database. The version running while this is applied still reads them, so they are dropped by the next
    /// migration, once no version maps them. This version saves rows without them: the stock columns and LoyaltyTier
    /// take NULL, and LoyaltyPoints its default 0.
    /// </summary>
    public partial class StopMappingStockAndLoyaltyColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
