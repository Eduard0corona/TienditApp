using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BackfillLegacyTenantCapabilities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE ts
                SET ts.Capabilities =
                    CASE t.BusinessType
                        WHEN 0 THEN 44 -- Food + Inventory + Kitchen
                        WHEN 1 THEN 9  -- Retail + Inventory
                        WHEN 2 THEN 10 -- Service + Inventory
                        ELSE ts.Capabilities
                    END
                FROM TenantSettings ts
                INNER JOIN Tenants t ON t.Id = ts.TenantId
                WHERE ts.Capabilities IN (0, 8);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: data backfill is intentionally irreversible.
        }
    }
}
