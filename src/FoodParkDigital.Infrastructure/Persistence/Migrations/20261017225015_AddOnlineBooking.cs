using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOnlineBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OnlineBookingCapacity",
                table: "TenantSettings",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "OnlineBookingEnabled",
                table: "TenantSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledByCustomerAt",
                table: "Appointments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Channel",
                table: "Appointments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "ClientRequestId",
                table: "Appointments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomerUserId",
                table: "Appointments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrackingToken",
                table: "Appointments",
                type: "varchar(43)",
                unicode: false,
                maxLength: 43,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_TenantSettings_OnlineBookingCapacity",
                table: "TenantSettings",
                sql: "[OnlineBookingCapacity] >= 1 AND [OnlineBookingCapacity] <= 20");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_CustomerUserId_StartAt",
                table: "Appointments",
                columns: new[] { "CustomerUserId", "StartAt" },
                filter: "[CustomerUserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_TenantId_ClientRequestId",
                table: "Appointments",
                columns: new[] { "TenantId", "ClientRequestId" },
                unique: true,
                filter: "[ClientRequestId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_TrackingToken",
                table: "Appointments",
                column: "TrackingToken",
                unique: true,
                filter: "[TrackingToken] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Appointments_Channel",
                table: "Appointments",
                sql: "[Channel] IN (0, 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Appointments_TrackingToken",
                table: "Appointments",
                sql: "[TrackingToken] IS NULL OR LEN([TrackingToken]) = 43");

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_AspNetUsers_CustomerUserId",
                table: "Appointments",
                column: "CustomerUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rolling back forgets which appointments customers booked online, their accounts and their links, and leaves
            // the requests in a state the previous version cannot decide. Once there is one there is only roll-forward. A
            // missing column compiles inside EXEC: a second run of rollback.sql skips it.
            migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.Appointments', N'Channel') IS NOT NULL
    EXEC(N'IF EXISTS (SELECT 1 FROM [Appointments] WHERE [Channel] = 1)
    THROW 50019, N''Customers booked appointments online; rolling back would lose them. Roll forward instead.'', 1;');
");

            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_AspNetUsers_CustomerUserId",
                table: "Appointments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TenantSettings_OnlineBookingCapacity",
                table: "TenantSettings");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_CustomerUserId_StartAt",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_TenantId_ClientRequestId",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_TrackingToken",
                table: "Appointments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Appointments_Channel",
                table: "Appointments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Appointments_TrackingToken",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "OnlineBookingCapacity",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "OnlineBookingEnabled",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "CancelledByCustomerAt",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "Channel",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "ClientRequestId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "CustomerUserId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "TrackingToken",
                table: "Appointments");
        }
    }
}
