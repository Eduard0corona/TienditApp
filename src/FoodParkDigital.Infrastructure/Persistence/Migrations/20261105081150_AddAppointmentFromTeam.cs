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

            migrationBuilder.AddColumn<string>(
                name: "RequestPhone",
                table: "Appointments",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            // The requests online that exist: the key of the phone each one has now (MessagingPhone.ToKey, +52 and ten digits
            // for a Mexican mobile written +521). In an EXEC: the idempotent script compiles each migration as one batch,
            // before the column exists.
            migrationBuilder.Sql(@"
EXEC(N'UPDATE [Appointments]
SET [RequestPhone] = CASE
    WHEN LEN(LTRIM(RTRIM([CustomerPhone]))) = 14 AND LTRIM(RTRIM([CustomerPhone])) LIKE N''+521%''
        THEN N''+52'' + SUBSTRING(LTRIM(RTRIM([CustomerPhone])), 5, 10)
    ELSE LTRIM(RTRIM([CustomerPhone]))
END
WHERE [Channel] = 1');");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_OnlineRequests_Customer",
                table: "Appointments",
                columns: new[] { "TenantId", "RequestPhone", "CreatedAt" },
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

            migrationBuilder.DropColumn(
                name: "RequestPhone",
                table: "Appointments");
        }
    }
}
