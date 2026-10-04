using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// IVA policy per business and per product, and closing of paid counter sales. Existing rows keep
    /// what they had: businesses keep IVA added on top of their prices (16 %, the only rate until now),
    /// every sold item records that 16 %, and counter sales start closing when paid.
    /// </summary>
    public partial class AddTaxPolicyAndCounterSaleClosing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoCloseCounterSales",
                table: "TenantSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DefaultTaxRate",
                table: "TenantSettings",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 16m);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OnboardingCompletedAt",
                table: "TenantSettings",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PricesIncludeTax",
                table: "TenantSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsTaxExempt",
                table: "Products",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PricesIncludeTax",
                table: "Orders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsTaxExempt",
                table: "OrderItems",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Items sold so far were taxed at 16 %: their totals are recomputed with the rate they had.
            migrationBuilder.AddColumn<decimal>(
                name: "TaxRate",
                table: "OrderItems",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 16m);

            // Businesses already selling are past the guided setup; only new or empty ones are offered it.
            // EXEC compiles the UPDATE when it runs: the idempotent script sends the whole migration as one
            // batch, and SQL Server rejects a batch that names a column added earlier in that batch.
            migrationBuilder.Sql("""
                EXEC(N'UPDATE [ts]
                SET [OnboardingCompletedAt] = SYSDATETIMEOFFSET()
                FROM [TenantSettings] AS [ts]
                WHERE [ts].[OnboardingCompletedAt] IS NULL
                  AND (EXISTS (SELECT 1 FROM [Products] AS [p] WHERE [p].[TenantId] = [ts].[TenantId])
                    OR EXISTS (SELECT 1 FROM [Orders] AS [o] WHERE [o].[TenantId] = [ts].[TenantId]))');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoCloseCounterSales",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "DefaultTaxRate",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "OnboardingCompletedAt",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "PricesIncludeTax",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "IsTaxExempt",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "PricesIncludeTax",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "IsTaxExempt",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "TaxRate",
                table: "OrderItems");
        }
    }
}
