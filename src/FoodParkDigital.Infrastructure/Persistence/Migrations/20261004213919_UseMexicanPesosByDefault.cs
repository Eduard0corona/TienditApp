using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UseMexicanPesosByDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every business got "USD" from the old default and nothing lets a business choose another currency:
            // the app charges and shows every amount in Mexican pesos. Prices and totals do not change.
            migrationBuilder.Sql("UPDATE [TenantSettings] SET [Currency] = 'MXN' WHERE [Currency] = 'USD';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The previous code wrote "USD" for every business, so going back restores that value.
            migrationBuilder.Sql("UPDATE [TenantSettings] SET [Currency] = 'USD' WHERE [Currency] = 'MXN';");
        }
    }
}
