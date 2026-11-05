using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentFromTeam : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "FromTeam",
                table: "Appointments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_OnlineRequests_Customer",
                table: "Appointments",
                columns: new[] { "TenantId", "CustomerPhone", "CreatedAt" },
                filter: "[Channel] = 1")
                .Annotation("SqlServer:Include", new[] { "RespondedAt", "Status", "StartAt", "RequestedStartAt", "CancelledByCustomerAt", "FromTeam" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Appointments_OnlineRequests_Customer",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "FromTeam",
                table: "Appointments");
        }
    }
}
