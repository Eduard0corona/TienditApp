using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentLocationAndEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EvidenceUrl",
                table: "OrderPayments",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InventoryLocationId",
                table: "OrderPayments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InventoryLocationId",
                table: "CashTransactions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderPayments_InventoryLocationId",
                table: "OrderPayments",
                column: "InventoryLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderPayments_TenantId_InventoryLocationId_PaidAt",
                table: "OrderPayments",
                columns: new[] { "TenantId", "InventoryLocationId", "PaidAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CashTransactions_InventoryLocationId",
                table: "CashTransactions",
                column: "InventoryLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_CashTransactions_TenantId_InventoryLocationId_OccurredAt",
                table: "CashTransactions",
                columns: new[] { "TenantId", "InventoryLocationId", "OccurredAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_CashTransactions_InventoryLocations_InventoryLocationId",
                table: "CashTransactions",
                column: "InventoryLocationId",
                principalTable: "InventoryLocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderPayments_InventoryLocations_InventoryLocationId",
                table: "OrderPayments",
                column: "InventoryLocationId",
                principalTable: "InventoryLocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CashTransactions_InventoryLocations_InventoryLocationId",
                table: "CashTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderPayments_InventoryLocations_InventoryLocationId",
                table: "OrderPayments");

            migrationBuilder.DropIndex(
                name: "IX_OrderPayments_InventoryLocationId",
                table: "OrderPayments");

            migrationBuilder.DropIndex(
                name: "IX_OrderPayments_TenantId_InventoryLocationId_PaidAt",
                table: "OrderPayments");

            migrationBuilder.DropIndex(
                name: "IX_CashTransactions_InventoryLocationId",
                table: "CashTransactions");

            migrationBuilder.DropIndex(
                name: "IX_CashTransactions_TenantId_InventoryLocationId_OccurredAt",
                table: "CashTransactions");

            migrationBuilder.DropColumn(
                name: "EvidenceUrl",
                table: "OrderPayments");

            migrationBuilder.DropColumn(
                name: "InventoryLocationId",
                table: "OrderPayments");

            migrationBuilder.DropColumn(
                name: "InventoryLocationId",
                table: "CashTransactions");
        }
    }
}
