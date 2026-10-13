using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCourierSettlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CashToCollect",
                table: "OrderDeliveries",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DispatchedAt",
                table: "OrderDeliveries",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DispatchedBy",
                table: "OrderDeliveries",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PaysWith",
                table: "OrderDeliveries",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CourierSettlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Courier = table.Column<int>(type: "int", nullable: false),
                    CashSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Method = table.Column<int>(type: "int", nullable: false),
                    HandedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OrdersTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AdjustmentsTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SettledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SettledBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourierSettlements", x => x.Id);
                    table.CheckConstraint("CK_CourierSettlements_Balances", "[HandedAmount] = [OrdersTotal] + [AdjustmentsTotal]");
                    table.CheckConstraint("CK_CourierSettlements_Courier", "[Courier] IN (1)");
                    table.CheckConstraint("CK_CourierSettlements_HandedAmount", "[HandedAmount] >= 0");
                    table.CheckConstraint("CK_CourierSettlements_Method", "[Method] IN (0, 2)");
                    table.ForeignKey(
                        name: "FK_CourierSettlements_CashSessions_CashSessionId",
                        column: x => x.CashSessionId,
                        principalTable: "CashSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CourierSettlementAdjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SettlementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourierSettlementAdjustments", x => x.Id);
                    table.CheckConstraint("CK_CourierSettlementAdjustments_Amount", "[Amount] <> 0 AND ([Kind] = 99 OR [Amount] < 0)");
                    table.CheckConstraint("CK_CourierSettlementAdjustments_Kind", "[Kind] IN (1, 2, 3, 99)");
                    table.CheckConstraint("CK_CourierSettlementAdjustments_Other", "[Kind] <> 99 OR [Description] IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_CourierSettlementAdjustments_CourierSettlements_SettlementId",
                        column: x => x.SettlementId,
                        principalTable: "CourierSettlements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CourierSettlementOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SettlementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourierSettlementOrders", x => x.Id);
                    table.CheckConstraint("CK_CourierSettlementOrders_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_CourierSettlementOrders_CourierSettlements_SettlementId",
                        column: x => x.SettlementId,
                        principalTable: "CourierSettlements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CourierSettlementOrders_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderDeliveries_CashToCollect",
                table: "OrderDeliveries",
                sql: "[CashToCollect] IS NULL OR ([CashToCollect] > 0 AND [DispatchedAt] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderDeliveries_Dispatch",
                table: "OrderDeliveries",
                sql: "[DispatchedAt] IS NULL OR [Courier] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderDeliveries_PaysWith",
                table: "OrderDeliveries",
                sql: "[PaysWith] IS NULL OR ([PaysWith] > 0 AND [PaysWith] <= 100000)");

            migrationBuilder.CreateIndex(
                name: "IX_CourierSettlementAdjustments_SettlementId",
                table: "CourierSettlementAdjustments",
                column: "SettlementId");

            migrationBuilder.CreateIndex(
                name: "IX_CourierSettlementOrders_OrderId",
                table: "CourierSettlementOrders",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourierSettlementOrders_SettlementId",
                table: "CourierSettlementOrders",
                column: "SettlementId");

            migrationBuilder.CreateIndex(
                name: "IX_CourierSettlementOrders_TenantId_OrderId",
                table: "CourierSettlementOrders",
                columns: new[] { "TenantId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_CourierSettlements_CashSessionId",
                table: "CourierSettlements",
                column: "CashSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_CourierSettlements_TenantId_SettledAt",
                table: "CourierSettlements",
                columns: new[] { "TenantId", "SettledAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // An order that went out would lose what the courier collects, so what the courier owes could no longer be told;
            // a settlement would lose which orders a handover paid and why it was short, while the payments and the movements
            // it registered stay in the orders and the drawers. With any of them saved there is only roll-forward. The check
            // runs through EXEC: a column that is already gone would not compile even inside a branch that never runs, so a
            // second run of rollback.sql (where this branch is skipped) still does nothing.
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [CourierSettlements])
    OR EXISTS (SELECT 1 FROM [OrderDeliveries] WHERE [DispatchedAt] IS NOT NULL OR [PaysWith] IS NOT NULL)
    THROW 50014, N''There are orders sent out with the courier or courier settlements; rolling back would lose what the courier collects and what it handed over. Roll forward instead.'', 1;');
");

            migrationBuilder.DropTable(
                name: "CourierSettlementAdjustments");

            migrationBuilder.DropTable(
                name: "CourierSettlementOrders");

            migrationBuilder.DropTable(
                name: "CourierSettlements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderDeliveries_CashToCollect",
                table: "OrderDeliveries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderDeliveries_Dispatch",
                table: "OrderDeliveries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderDeliveries_PaysWith",
                table: "OrderDeliveries");

            migrationBuilder.DropColumn(
                name: "CashToCollect",
                table: "OrderDeliveries");

            migrationBuilder.DropColumn(
                name: "DispatchedAt",
                table: "OrderDeliveries");

            migrationBuilder.DropColumn(
                name: "DispatchedBy",
                table: "OrderDeliveries");

            migrationBuilder.DropColumn(
                name: "PaysWith",
                table: "OrderDeliveries");
        }
    }
}
