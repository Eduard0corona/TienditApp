using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOfflineSaleIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CapturedOfflineAt",
                table: "Orders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClientRequestId",
                table: "Orders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TenantId_ClientRequestId",
                table: "Orders",
                columns: new[] { "TenantId", "ClientRequestId" },
                unique: true,
                filter: "[ClientRequestId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // When a sale was captured without connection is part of its history; the keys alone are only for retries.
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM [Orders] WHERE [CapturedOfflineAt] IS NOT NULL)
    THROW 50005, N'There are sales captured without connection; rolling back would lose when they were captured.', 1;
");

            migrationBuilder.DropIndex(
                name: "IX_Orders_TenantId_ClientRequestId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CapturedOfflineAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ClientRequestId",
                table: "Orders");
        }
    }
}
