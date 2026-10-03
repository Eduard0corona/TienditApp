using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InventoryConcurrencyTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Model-only change: InventoryStock.OnHandQuantity, SupplyItem.AverageCost and
            // PurchaseOrderLine.ReceivedQuantity become optimistic concurrency tokens (EF adds the original
            // value to the UPDATE's WHERE clause). The schema does not change.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
