using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentRespondedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RespondedAt",
                table: "Appointments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_OnlineRequests_CreatedAt",
                table: "Appointments",
                column: "CreatedAt",
                filter: "[Channel] = 1")
                .Annotation("SqlServer:Include", new[] { "TenantId", "RespondedAt", "Status", "StartAt", "CancelledByCustomerAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Appointments_OnlineRequests_CreatedAt",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "RespondedAt",
                table: "Appointments");
        }
    }
}
