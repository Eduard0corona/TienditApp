using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderDeliveries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrderDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PostalCode = table.Column<string>(type: "varchar(5)", unicode: false, maxLength: 5, nullable: false),
                    AddressLine = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    References = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ZoneName = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Fee = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    FeeTaxRate = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    Courier = table.Column<int>(type: "int", nullable: true),
                    CourierFolio = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    TrackingUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ShipmentRegisteredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ShipmentRegisteredBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderDeliveries", x => x.Id);
                    table.CheckConstraint("CK_OrderDeliveries_AddressLine", "LEN([AddressLine]) > 0");
                    table.CheckConstraint("CK_OrderDeliveries_Fee", "[Fee] >= 0 AND [Fee] <= 10000");
                    table.CheckConstraint("CK_OrderDeliveries_FeeTaxRate", "[FeeTaxRate] >= 0 AND [FeeTaxRate] <= 100");
                    table.CheckConstraint("CK_OrderDeliveries_PostalCode", "LEN([PostalCode]) = 5 AND [PostalCode] NOT LIKE '%[^0-9]%'");
                    table.CheckConstraint("CK_OrderDeliveries_Shipment", "([Courier] IS NULL AND [CourierFolio] IS NULL AND [TrackingUrl] IS NULL AND [ShipmentRegisteredAt] IS NULL) OR ([Courier] IS NOT NULL AND [Courier] = 1 AND [CourierFolio] IS NOT NULL AND LEN([CourierFolio]) > 0 AND [ShipmentRegisteredAt] IS NOT NULL)");
                    table.CheckConstraint("CK_OrderDeliveries_TrackingUrl", "[TrackingUrl] IS NULL OR [TrackingUrl] LIKE 'https://_%'");
                    table.ForeignKey(
                        name: "FK_OrderDeliveries_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderDeliveries_OrderId",
                table: "OrderDeliveries",
                column: "OrderId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Orders sent to an address would lose where they go and what their delivery charged, while their totals keep
            // the fee: the version before would show totals its items do not add up to. With deliveries saved there is
            // only roll-forward. A table that is already gone compiles all the same (deferred name resolution), so a
            // second run of rollback.sql does nothing.
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM [OrderDeliveries])
    THROW 50013, N'There are orders sent to an address; rolling back would lose their address and delivery fee. Roll forward instead.', 1;
");

            migrationBuilder.DropTable(
                name: "OrderDeliveries");
        }
    }
}
