using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformPrices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductOptionPlatformPrices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Channel = table.Column<int>(type: "int", nullable: false),
                    PriceDelta = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductOptionPlatformPrices", x => x.Id);
                    table.CheckConstraint("CK_ProductOptionPlatformPrices_Channel", "[Channel] IN (3, 4, 5)");
                    table.CheckConstraint("CK_ProductOptionPlatformPrices_PriceDelta", "[PriceDelta] BETWEEN -100000 AND 100000");
                    table.ForeignKey(
                        name: "FK_ProductOptionPlatformPrices_ProductOptions_OptionId",
                        column: x => x.OptionId,
                        principalTable: "ProductOptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductPlatformPrices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Channel = table.Column<int>(type: "int", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductPlatformPrices", x => x.Id);
                    table.CheckConstraint("CK_ProductPlatformPrices_Channel", "[Channel] IN (3, 4, 5)");
                    table.CheckConstraint("CK_ProductPlatformPrices_Price", "[Price] > 0");
                    table.ForeignKey(
                        name: "FK_ProductPlatformPrices_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductOptionPlatformPrices_OptionId_Channel",
                table: "ProductOptionPlatformPrices",
                columns: new[] { "OptionId", "Channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductOptionPlatformPrices_TenantId_Channel",
                table: "ProductOptionPlatformPrices",
                columns: new[] { "TenantId", "Channel" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductPlatformPrices_ProductId_Channel",
                table: "ProductPlatformPrices",
                columns: new[] { "ProductId", "Channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductPlatformPrices_TenantId_Channel",
                table: "ProductPlatformPrices",
                columns: new[] { "TenantId", "Channel" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The prices the businesses set for the apps would be lost: unlike the terms of a platform, no activity entry
            // keeps them. With prices saved there is only roll-forward, or exporting and deleting them on purpose first.
            // The orders keep the prices they copied either way.
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM [ProductPlatformPrices])
   OR EXISTS (SELECT 1 FROM [ProductOptionPlatformPrices])
    THROW 50009, N'There are prices of delivery platforms saved; rolling back would delete them. Export and delete them first, or roll forward.', 1;
");

            migrationBuilder.DropTable(
                name: "ProductOptionPlatformPrices");

            migrationBuilder.DropTable(
                name: "ProductPlatformPrices");
        }
    }
}
