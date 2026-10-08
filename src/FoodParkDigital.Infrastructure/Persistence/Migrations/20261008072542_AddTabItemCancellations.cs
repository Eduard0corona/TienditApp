using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTabItemCancellations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CancelledAmount",
                table: "OrderItems",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CancelledQuantity",
                table: "OrderItems",
                type: "decimal(18,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "OrderItemCancellations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Round = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ReturnedToStock = table.Column<bool>(type: "bit", nullable: false),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CancelledBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderItemCancellations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderItemCancellations_OrderItems_OrderItemId",
                        column: x => x.OrderItemId,
                        principalTable: "OrderItems",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrderItemCancellations_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderItemCancellations_OrderId",
                table: "OrderItemCancellations",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItemCancellations_OrderItemId",
                table: "OrderItemCancellations",
                column: "OrderItemId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItemCancellations_TenantId_CancelledAt",
                table: "OrderItemCancellations",
                columns: new[] { "TenantId", "CancelledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderItemCancellations_TenantId_OrderId",
                table: "OrderItemCancellations",
                columns: new[] { "TenantId", "OrderId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderItemCancellations");

            migrationBuilder.DropColumn(
                name: "CancelledAmount",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "CancelledQuantity",
                table: "OrderItems");
        }
    }
}
