using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderCancellationEmailsAndAnswerReminder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "OrderCancellationEmails",
                table: "TenantUsers",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AnswerReminderMinutes",
                table: "TenantSettings",
                type: "int",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.AddCheckConstraint(
                name: "CK_TenantSettings_AnswerReminderMinutes",
                table: "TenantSettings",
                sql: "[AnswerReminderMinutes] IN (0, 30, 60, 120, 180)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TenantSettings_AnswerReminderMinutes",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "OrderCancellationEmails",
                table: "TenantUsers");

            migrationBuilder.DropColumn(
                name: "AnswerReminderMinutes",
                table: "TenantSettings");
        }
    }
}
