using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentWorkOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "WorkOrderId",
                table: "Appointments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_WorkOrderId",
                table: "Appointments",
                column: "WorkOrderId",
                filter: "[WorkOrderId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Appointments_Work",
                table: "Appointments",
                sql: "[WorkOrderId] IS NULL OR ([SaleOrderId] IS NULL AND [Channel] = 0 AND [TrackingToken] IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_Orders_WorkOrderId",
                table: "Appointments",
                column: "WorkOrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // An appointment that does the job of an order would become a plain appointment that could be charged again (Fase 29F).
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [Appointments] WHERE [WorkOrderId] IS NOT NULL)
    THROW 50054, N''Appointments already do the job of service orders; rolling back would forget which order each one belongs to and let it be charged again. Roll forward instead.'', 1;');
");

            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_Orders_WorkOrderId",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_WorkOrderId",
                table: "Appointments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Appointments_Work",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "WorkOrderId",
                table: "Appointments");
        }
    }
}
