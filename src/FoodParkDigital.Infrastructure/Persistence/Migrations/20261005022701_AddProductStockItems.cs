using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductStockItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProductId",
                table: "SupplyItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplyItems_ProductId",
                table: "SupplyItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplyItems_TenantId_ProductId",
                table: "SupplyItems",
                columns: new[] { "TenantId", "ProductId" },
                unique: true,
                filter: "[ProductId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_SupplyItems_Products_ProductId",
                table: "SupplyItems",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SupplyItems_Products_ProductId",
                table: "SupplyItems");

            migrationBuilder.DropIndex(
                name: "IX_SupplyItems_ProductId",
                table: "SupplyItems");

            migrationBuilder.DropIndex(
                name: "IX_SupplyItems_TenantId_ProductId",
                table: "SupplyItems");

            migrationBuilder.DropColumn(
                name: "ProductId",
                table: "SupplyItems");
        }
    }
}
