using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformSettlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlatformSettlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Channel = table.Column<int>(type: "int", nullable: false),
                    PeriodFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodTo = table.Column<DateOnly>(type: "date", nullable: false),
                    DepositedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    DepositedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    OrdersTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AdjustmentsTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VoidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformSettlements", x => x.Id);
                    table.CheckConstraint("CK_PlatformSettlements_Channel", "[Channel] IN (3, 4, 5)");
                    table.CheckConstraint("CK_PlatformSettlements_Closed", "[Status] <> 1 OR [ClosedAt] IS NOT NULL");
                    table.CheckConstraint("CK_PlatformSettlements_Dates", "[PeriodTo] >= [PeriodFrom] AND [DepositedOn] >= [PeriodFrom]");
                    table.CheckConstraint("CK_PlatformSettlements_Revision", "[Revision] >= 1");
                    table.CheckConstraint("CK_PlatformSettlements_Status", "[Status] IN (0, 1, 99)");
                    table.CheckConstraint("CK_PlatformSettlements_Voided", "[Status] <> 99 OR ([VoidedAt] IS NOT NULL AND [VoidReason] IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "PlatformSettlementAdjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SettlementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformSettlementAdjustments", x => x.Id);
                    table.CheckConstraint("CK_PlatformSettlementAdjustments_Amount", "[Amount] <> 0");
                    table.CheckConstraint("CK_PlatformSettlementAdjustments_Kind", "[Kind] IN (1, 2, 3, 4, 5, 99)");
                    table.CheckConstraint("CK_PlatformSettlementAdjustments_Other", "[Kind] <> 99 OR [Description] IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_PlatformSettlementAdjustments_PlatformSettlements_SettlementId",
                        column: x => x.SettlementId,
                        principalTable: "PlatformSettlements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlatformSettlementOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SettlementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpectedNet = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformSettlementOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformSettlementOrders_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlatformSettlementOrders_PlatformSettlements_SettlementId",
                        column: x => x.SettlementId,
                        principalTable: "PlatformSettlements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformSettlementAdjustments_SettlementId",
                table: "PlatformSettlementAdjustments",
                column: "SettlementId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformSettlementOrders_OrderId_Active",
                table: "PlatformSettlementOrders",
                column: "OrderId",
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformSettlementOrders_SettlementId",
                table: "PlatformSettlementOrders",
                column: "SettlementId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformSettlementOrders_TenantId_OrderId",
                table: "PlatformSettlementOrders",
                columns: new[] { "TenantId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformSettlements_TenantId_Channel_Open",
                table: "PlatformSettlements",
                columns: new[] { "TenantId", "Channel" },
                unique: true,
                filter: "[Status] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformSettlements_TenantId_PeriodTo",
                table: "PlatformSettlements",
                columns: new[] { "TenantId", "PeriodTo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The settlements of the businesses would be lost: no activity entry keeps their orders or adjustments, and
            // their orders would show as pending again. With settlements saved there is only roll-forward, or exporting
            // and deleting them on purpose first. The orders themselves never change either way.
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM [PlatformSettlements])
    THROW 50010, N'There are settlements of delivery platforms saved; rolling back would delete them. Export and delete them first, or roll forward.', 1;
");

            migrationBuilder.DropTable(
                name: "PlatformSettlementAdjustments");

            migrationBuilder.DropTable(
                name: "PlatformSettlementOrders");

            migrationBuilder.DropTable(
                name: "PlatformSettlements");
        }
    }
}
