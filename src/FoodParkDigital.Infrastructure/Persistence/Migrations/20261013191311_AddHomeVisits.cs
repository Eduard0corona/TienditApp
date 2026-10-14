using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHomeVisits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HomeVisitTravelMinutes",
                table: "TenantSettings",
                type: "int",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<Guid>(
                name: "SaleOrderId",
                table: "Appointments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "Version",
                table: "Appointments",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateTable(
                name: "AppointmentVisits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PostalCode = table.Column<string>(type: "varchar(5)", unicode: false, maxLength: 5, nullable: false),
                    AddressLine = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    References = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ZoneName = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Fee = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentVisits", x => x.Id);
                    table.CheckConstraint("CK_AppointmentVisits_AddressLine", "LEN([AddressLine]) > 0");
                    table.CheckConstraint("CK_AppointmentVisits_Fee", "[Fee] >= 0 AND [Fee] <= 10000");
                    table.CheckConstraint("CK_AppointmentVisits_PostalCode", "LEN([PostalCode]) = 5 AND [PostalCode] NOT LIKE '%[^0-9]%'");
                    table.ForeignKey(
                        name: "FK_AppointmentVisits_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrderVisitCharges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZoneName = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Fee = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    FeeTaxRate = table.Column<decimal>(type: "decimal(5,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderVisitCharges", x => x.Id);
                    table.CheckConstraint("CK_OrderVisitCharges_Fee", "[Fee] > 0 AND [Fee] <= 10000");
                    table.CheckConstraint("CK_OrderVisitCharges_FeeTaxRate", "[FeeTaxRate] >= 0 AND [FeeTaxRate] <= 100");
                    table.ForeignKey(
                        name: "FK_OrderVisitCharges_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderVisitCharges_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_TenantSettings_HomeVisitTravelMinutes",
                table: "TenantSettings",
                sql: "[HomeVisitTravelMinutes] >= 0 AND [HomeVisitTravelMinutes] <= 240");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_SaleOrderId",
                table: "Appointments",
                column: "SaleOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentVisits_AppointmentId",
                table: "AppointmentVisits",
                column: "AppointmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderVisitCharges_AppointmentId",
                table: "OrderVisitCharges",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderVisitCharges_OrderId",
                table: "OrderVisitCharges",
                column: "OrderId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_Orders_SaleOrderId",
                table: "Appointments",
                column: "SaleOrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A visit would lose the customer's address and the charge agreed, and a sale would lose its visit charge (its
            // total keeps it, but the concept would no longer be told apart) and which appointment it charged. With any of
            // them saved there is only roll-forward. The check runs through EXEC: a column that is already gone would not
            // compile even inside a branch that never runs, so a second run of rollback.sql (where this branch is skipped)
            // still does nothing.
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [AppointmentVisits])
    OR EXISTS (SELECT 1 FROM [OrderVisitCharges])
    OR EXISTS (SELECT 1 FROM [Appointments] WHERE [SaleOrderId] IS NOT NULL)
    THROW 50015, N''There are home visits or charged appointments; rolling back would lose their addresses, visit charges and sales. Roll forward instead.'', 1;');
");

            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_Orders_SaleOrderId",
                table: "Appointments");

            migrationBuilder.DropTable(
                name: "AppointmentVisits");

            migrationBuilder.DropTable(
                name: "OrderVisitCharges");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TenantSettings_HomeVisitTravelMinutes",
                table: "TenantSettings");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_SaleOrderId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "HomeVisitTravelMinutes",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "SaleOrderId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Appointments");
        }
    }
}
