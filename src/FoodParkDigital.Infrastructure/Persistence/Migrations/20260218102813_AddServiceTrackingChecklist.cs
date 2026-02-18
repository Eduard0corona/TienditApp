using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceTrackingChecklist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsTrackable",
                table: "ServiceOrderPieces",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyCustomer",
                table: "ServiceOrderPieces",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ServiceProductId",
                table: "ServiceOrderPieces",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsTrackable",
                table: "Products",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyCustomer",
                table: "Products",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOrderPieces_TenantId_IsTrackable",
                table: "ServiceOrderPieces",
                columns: new[] { "TenantId", "IsTrackable" });

            migrationBuilder.Sql("""
                UPDATE ServiceOrderPieces
                SET Status = CASE
                    WHEN Status = 0 THEN 0          -- Received -> Accepted
                    WHEN Status IN (1,2,3,4,5) THEN 1 -- Inspection/Washing/... -> InProgress
                    WHEN Status = 6 THEN 2          -- Delivered -> Completed
                    WHEN Status = 99 THEN 99        -- Cancelled
                    ELSE 0
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ServiceOrderPieces_TenantId_IsTrackable",
                table: "ServiceOrderPieces");

            migrationBuilder.DropColumn(
                name: "IsTrackable",
                table: "ServiceOrderPieces");

            migrationBuilder.DropColumn(
                name: "NotifyCustomer",
                table: "ServiceOrderPieces");

            migrationBuilder.DropColumn(
                name: "ServiceProductId",
                table: "ServiceOrderPieces");

            migrationBuilder.DropColumn(
                name: "IsTrackable",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "NotifyCustomer",
                table: "Products");
        }
    }
}
