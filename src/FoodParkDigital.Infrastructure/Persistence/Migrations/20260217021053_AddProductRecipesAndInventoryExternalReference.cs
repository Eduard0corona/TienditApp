using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductRecipesAndInventoryExternalReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductSupplyRequirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplyItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InventoryLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    WastePercent = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductSupplyRequirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductSupplyRequirements_InventoryLocations_InventoryLocationId",
                        column: x => x.InventoryLocationId,
                        principalTable: "InventoryLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductSupplyRequirements_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductSupplyRequirements_SupplyItems_SupplyItemId",
                        column: x => x.SupplyItemId,
                        principalTable: "SupplyItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_TenantId_ExternalReference",
                table: "InventoryMovements",
                columns: new[] { "TenantId", "ExternalReference" },
                unique: true,
                filter: "[ExternalReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProductSupplyRequirements_InventoryLocationId",
                table: "ProductSupplyRequirements",
                column: "InventoryLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductSupplyRequirements_ProductId",
                table: "ProductSupplyRequirements",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductSupplyRequirements_SupplyItemId",
                table: "ProductSupplyRequirements",
                column: "SupplyItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductSupplyRequirements_TenantId_InventoryLocationId",
                table: "ProductSupplyRequirements",
                columns: new[] { "TenantId", "InventoryLocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductSupplyRequirements_TenantId_ProductId",
                table: "ProductSupplyRequirements",
                columns: new[] { "TenantId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductSupplyRequirements_TenantId_ProductId_SupplyItemId_InventoryLocationId",
                table: "ProductSupplyRequirements",
                columns: new[] { "TenantId", "ProductId", "SupplyItemId", "InventoryLocationId" },
                unique: true,
                filter: "[InventoryLocationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProductSupplyRequirements_TenantId_ProductId_SupplyItemId_IsActive",
                table: "ProductSupplyRequirements",
                columns: new[] { "TenantId", "ProductId", "SupplyItemId", "IsActive" },
                filter: "[InventoryLocationId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProductSupplyRequirements_TenantId_SupplyItemId",
                table: "ProductSupplyRequirements",
                columns: new[] { "TenantId", "SupplyItemId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductSupplyRequirements");

            migrationBuilder.DropIndex(
                name: "IX_InventoryMovements_TenantId_ExternalReference",
                table: "InventoryMovements");
        }
    }
}
