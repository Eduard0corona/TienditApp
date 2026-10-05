using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentResourcesAndStockPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "InsufficientStockPolicy",
                table: "TenantSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "ServiceProductId",
                table: "Appointments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StaffUserId",
                table: "Appointments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_TenantId_StaffUserId_StartAt",
                table: "Appointments",
                columns: new[] { "TenantId", "StaffUserId", "StartAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Appointments_TenantId_StaffUserId_StartAt",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "InsufficientStockPolicy",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "ServiceProductId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "StaffUserId",
                table: "Appointments");
        }
    }
}
