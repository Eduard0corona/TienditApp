using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnableRetailForServiceTenants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE ts
                SET ts.Capabilities = ts.Capabilities | 1 -- Retail
                FROM TenantSettings ts
                INNER JOIN Tenants t ON t.Id = ts.TenantId
                WHERE t.BusinessType = 2                 -- Service
                  AND (ts.Capabilities & 2) = 2          -- Has Service
                  AND (ts.Capabilities & 1) = 0;         -- Missing Retail
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: data backfill is intentionally irreversible.
        }
    }
}
