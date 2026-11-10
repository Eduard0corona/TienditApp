using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Fase 31H (ADR 0088): the links that point to nothing go before the keys check them: a product deleted before
            // this version (the appointment keeps the name it was booked with, as deleting one does from now on) or an
            // account that does not exist. Queries 3 and 4 of verify.sql list them first. The locks hold until the keys
            // exist, in the same transaction: no appointment is saved in between, and no product the deployed version deletes
            // meanwhile leaves a new one behind.
            migrationBuilder.Sql(@"
EXEC(N'UPDATE a SET [ServiceProductId] = NULL
FROM [Appointments] a WITH (TABLOCKX, HOLDLOCK)
WHERE a.[ServiceProductId] IS NOT NULL
    AND NOT EXISTS (SELECT 1 FROM [Products] p WITH (TABLOCK, HOLDLOCK) WHERE p.[Id] = a.[ServiceProductId]);
UPDATE a SET [StaffUserId] = NULL
FROM [Appointments] a
WHERE a.[StaffUserId] IS NOT NULL
    AND NOT EXISTS (SELECT 1 FROM [AspNetUsers] u WHERE u.[Id] = a.[StaffUserId]);');
");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ServiceProductId",
                table: "Appointments",
                column: "ServiceProductId",
                filter: "[ServiceProductId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_StaffUserId",
                table: "Appointments",
                column: "StaffUserId",
                filter: "[StaffUserId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_AspNetUsers_StaffUserId",
                table: "Appointments",
                column: "StaffUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_Products_ServiceProductId",
                table: "Appointments",
                column: "ServiceProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only the keys and their indexes go; the links Up emptied stay empty (they pointed to nothing).
            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_AspNetUsers_StaffUserId",
                table: "Appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_Products_ServiceProductId",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_ServiceProductId",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_StaffUserId",
                table: "Appointments");
        }
    }
}
