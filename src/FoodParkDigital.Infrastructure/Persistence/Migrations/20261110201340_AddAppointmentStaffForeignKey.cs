using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentStaffForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Fase 31H (ADR 0088): an appointment whose team member points to an account that does not exist stops it before
            // anything changes. Accounts are never deleted, so that would be some other Id: look into it (query 3 of its
            // verify.sql) before emptying it, which leaves the name in StaffName. The lock holds until the key exists, in the
            // same transaction: no appointment the deployed version saves in between escapes the check.
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [Appointments] a WITH (TABLOCKX, HOLDLOCK)
    WHERE a.[StaffUserId] IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [AspNetUsers] u WHERE u.[Id] = a.[StaffUserId]))
    THROW 50058, N''An appointment points to a team member whose account does not exist; look into it (query 3 of verify.sql) before applying this migration.'', 1;');
");

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_AspNetUsers_StaffUserId",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_StaffUserId",
                table: "Appointments");
        }
    }
}
