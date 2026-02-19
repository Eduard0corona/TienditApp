using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCriticalSupplyAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsCritical",
                table: "SupplyItems",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_SupplyItems_TenantId_IsCritical",
                table: "SupplyItems",
                columns: new[] { "TenantId", "IsCritical" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SupplyItems_TenantId_IsCritical",
                table: "SupplyItems");

            migrationBuilder.DropColumn(
                name: "IsCritical",
                table: "SupplyItems");
        }
    }
}
