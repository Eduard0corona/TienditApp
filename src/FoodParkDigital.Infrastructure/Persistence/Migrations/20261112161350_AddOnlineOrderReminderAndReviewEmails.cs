using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOnlineOrderReminderAndReviewEmails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ReviewEmails",
                table: "TenantUsers",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConfirmReminderSentAt",
                table: "Orders",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Fase 31K (ADR 0091): like the choices of the Fase 30B, rolling back forgets who chose the emails of new reviews
            // (the owners get none from the version before) and which orders were reminded; nothing a customer wrote is lost.
            migrationBuilder.DropColumn(
                name: "ReviewEmails",
                table: "TenantUsers");

            migrationBuilder.DropColumn(
                name: "ConfirmReminderSentAt",
                table: "Orders");
        }
    }
}
