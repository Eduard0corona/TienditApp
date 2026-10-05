using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseOrderClosing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CloseReason",
                table: "PurchaseOrders",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClosedAt",
                table: "PurchaseOrders",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A closed order (status 4) says the rest will not arrive; the previous code does not know that status and
            // would show it as pending. The guard only names [Status], so the batch compiles when run again.
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM [PurchaseOrders] WHERE [Status] = 4)
    THROW 50007, N'There are purchase orders closed without the remainder; rolling back would lose why and when they were closed.', 1;
");

            migrationBuilder.DropColumn(
                name: "CloseReason",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "ClosedAt",
                table: "PurchaseOrders");
        }
    }
}
