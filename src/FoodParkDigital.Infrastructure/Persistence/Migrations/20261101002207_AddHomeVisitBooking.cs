using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHomeVisitBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ChargePolicy",
                table: "AppointmentVisits",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerEmail",
                table: "Appointments",
                type: "nvarchar(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppointmentVisits_ChargePolicy",
                table: "AppointmentVisits",
                sql: "[ChargePolicy] IS NULL OR ([ChargePolicy] >= 0 AND [ChargePolicy] <= 2 AND ([ChargePolicy] <> 1 OR [Fee] = 0))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rolling back loses the visits customers requested online: how each is charged, as agreed, and where its quote
            // goes. Once there is one, there is only roll-forward. Through EXEC: on a second run of rollback.sql the columns
            // are gone, and a batch that names them would not compile.
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [AppointmentVisits] WHERE [ChargePolicy] IS NOT NULL)
    OR EXISTS (SELECT 1 FROM [Appointments] WHERE [CustomerEmail] IS NOT NULL)
    THROW 50050, N''Customers already requested visits to quote online; rolling back would lose how they are charged and where their quote goes. Roll forward instead.'', 1;');
");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppointmentVisits_ChargePolicy",
                table: "AppointmentVisits");

            migrationBuilder.DropColumn(
                name: "ChargePolicy",
                table: "AppointmentVisits");

            migrationBuilder.DropColumn(
                name: "CustomerEmail",
                table: "Appointments");
        }
    }
}
