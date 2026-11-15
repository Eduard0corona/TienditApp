using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentDeposits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_OnlinePaymentRefunds_Link",
                table: "OnlinePaymentRefunds");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OnlineCheckouts_Outcome",
                table: "OnlineCheckouts");

            migrationBuilder.AddColumn<int>(
                name: "BookingDepositPercent",
                table: "TenantSettings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AppointmentId",
                table: "OnlineCheckouts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ForAppointment",
                table: "OnlineCheckouts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "DepositAmount",
                table: "Appointments",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DepositStatus",
                table: "Appointments",
                type: "int",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_TenantSettings_BookingDepositPercent",
                table: "TenantSettings",
                sql: "[BookingDepositPercent] IS NULL OR [BookingDepositPercent] BETWEEN 10 AND 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OnlinePaymentRefunds_Link",
                table: "OnlinePaymentRefunds",
                sql: "([Reason] IN (1, 2, 7) AND [CheckoutId] IS NOT NULL AND [OrderId] IS NULL AND [OrderRefundId] IS NULL) OR ([Reason] IN (3, 4, 5, 6) AND [CheckoutId] IS NULL AND [OrderId] IS NOT NULL AND [OrderRefundId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_OnlineCheckouts_AppointmentId",
                table: "OnlineCheckouts",
                column: "AppointmentId",
                unique: true,
                filter: "[AppointmentId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OnlineCheckouts_Appointment",
                table: "OnlineCheckouts",
                sql: "([ForAppointment] = 0 AND [AppointmentId] IS NULL AND ([Status] <> 1 OR [OrderId] IS NOT NULL)) OR ([ForAppointment] = 1 AND [QuoteId] IS NULL AND ([Status] <> 1 OR [AppointmentId] IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OnlineCheckouts_Outcome",
                table: "OnlineCheckouts",
                sql: "([Status] = 0 AND ([Request] IS NOT NULL OR [QuoteId] IS NOT NULL) AND [PaymentId] IS NULL AND [OrderId] IS NULL AND [AppointmentId] IS NULL AND [Failure] IS NULL) OR ([Status] = 1 AND [Request] IS NULL AND [PaymentId] IS NOT NULL AND [PaidAt] IS NOT NULL AND ([OrderId] IS NOT NULL OR [AppointmentId] IS NOT NULL) AND [Failure] IS NULL) OR ([Status] = 2 AND [Request] IS NULL AND [PaymentId] IS NULL AND [OrderId] IS NULL AND [AppointmentId] IS NULL AND [Failure] IS NULL) OR ([Status] = 3 AND [Request] IS NULL AND [PaymentId] IS NOT NULL AND [PaidAt] IS NOT NULL AND [OrderId] IS NULL AND [AppointmentId] IS NULL AND [Failure] IS NOT NULL AND [Failure] BETWEEN 1 AND 4)");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_DepositHeld_Requests",
                table: "Appointments",
                columns: new[] { "StartAt", "TenantId" },
                filter: "[DepositStatus] = 1 AND [Status] = 5");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Appointments_Deposit",
                table: "Appointments",
                sql: "([DepositAmount] IS NULL AND [DepositStatus] IS NULL) OR ([DepositAmount] IS NOT NULL AND [DepositStatus] IS NOT NULL AND [DepositAmount] > 0 AND [DepositStatus] IN (1, 2, 3, 4) AND [Channel] = 1)");

            migrationBuilder.AddForeignKey(
                name: "FK_OnlineCheckouts_Appointments_AppointmentId",
                table: "OnlineCheckouts",
                column: "AppointmentId",
                principalTable: "Appointments",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Fase 31O (ADR 0095): rolling back would lose a deposit a customer paid to book an appointment (where it stands, and
            // its refund), or the share a business chose. Once there is one, it stops before anything changes; query 2 of
            // verify.sql counts them. Without any, it drops only what Up added.
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [OnlineCheckouts] WITH (TABLOCKX, HOLDLOCK) WHERE [ForAppointment] = 1)
    OR EXISTS (SELECT 1 FROM [Appointments] WITH (TABLOCKX, HOLDLOCK) WHERE [DepositAmount] IS NOT NULL)
    OR EXISTS (SELECT 1 FROM [TenantSettings] WITH (TABLOCKX, HOLDLOCK) WHERE [BookingDepositPercent] IS NOT NULL)
    THROW 50063, N''A business asks a deposit to book appointments online or a customer paid one; rolling back would lose it (query 2 of verify.sql).'', 1;');
");

            migrationBuilder.DropForeignKey(
                name: "FK_OnlineCheckouts_Appointments_AppointmentId",
                table: "OnlineCheckouts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TenantSettings_BookingDepositPercent",
                table: "TenantSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OnlinePaymentRefunds_Link",
                table: "OnlinePaymentRefunds");

            migrationBuilder.DropIndex(
                name: "IX_OnlineCheckouts_AppointmentId",
                table: "OnlineCheckouts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OnlineCheckouts_Appointment",
                table: "OnlineCheckouts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OnlineCheckouts_Outcome",
                table: "OnlineCheckouts");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_DepositHeld_Requests",
                table: "Appointments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Appointments_Deposit",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "BookingDepositPercent",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "AppointmentId",
                table: "OnlineCheckouts");

            migrationBuilder.DropColumn(
                name: "ForAppointment",
                table: "OnlineCheckouts");

            migrationBuilder.DropColumn(
                name: "DepositAmount",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "DepositStatus",
                table: "Appointments");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OnlinePaymentRefunds_Link",
                table: "OnlinePaymentRefunds",
                sql: "([Reason] IN (1, 2) AND [CheckoutId] IS NOT NULL AND [OrderId] IS NULL AND [OrderRefundId] IS NULL) OR ([Reason] IN (3, 4, 5, 6) AND [CheckoutId] IS NULL AND [OrderId] IS NOT NULL AND [OrderRefundId] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OnlineCheckouts_Outcome",
                table: "OnlineCheckouts",
                sql: "([Status] = 0 AND ([Request] IS NOT NULL OR [QuoteId] IS NOT NULL) AND [PaymentId] IS NULL AND [OrderId] IS NULL AND [Failure] IS NULL) OR ([Status] = 1 AND [Request] IS NULL AND [PaymentId] IS NOT NULL AND [PaidAt] IS NOT NULL AND [OrderId] IS NOT NULL AND [Failure] IS NULL) OR ([Status] = 2 AND [Request] IS NULL AND [PaymentId] IS NULL AND [OrderId] IS NULL AND [Failure] IS NULL) OR ([Status] = 3 AND [Request] IS NULL AND [PaymentId] IS NOT NULL AND [PaidAt] IS NOT NULL AND [OrderId] IS NULL AND [Failure] IS NOT NULL AND [Failure] BETWEEN 1 AND 4)");
        }
    }
}
