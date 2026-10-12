using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryPlatformOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PlatformAmount",
                table: "DailySalesSnapshots",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "DeliveryPlatformSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Channel = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CommissionPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    CommissionBase = table.Column<int>(type: "int", nullable: false),
                    CommissionVatPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    IsrWithholdingPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    VatWithholdingPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryPlatformSettings", x => x.Id);
                    table.CheckConstraint("CK_DeliveryPlatformSettings_Channel", "[Channel] IN (3, 4, 5)");
                    table.CheckConstraint("CK_DeliveryPlatformSettings_CommissionBase", "[CommissionBase] IN (0, 1)");
                    table.CheckConstraint("CK_DeliveryPlatformSettings_Percents", "[CommissionPercent] BETWEEN 0 AND 100 AND [CommissionVatPercent] BETWEEN 0 AND 100 AND [IsrWithholdingPercent] BETWEEN 0 AND 100 AND [VatWithholdingPercent] BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_DeliveryPlatformSettings_Revision", "[Revision] >= 1");
                });

            migrationBuilder.CreateTable(
                name: "PlatformOrderDetails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Channel = table.Column<int>(type: "int", nullable: false),
                    ExternalOrderNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CollectedBy = table.Column<int>(type: "int", nullable: false),
                    SettingsRevision = table.Column<int>(type: "int", nullable: false),
                    CommissionPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    CommissionBase = table.Column<int>(type: "int", nullable: false),
                    CommissionVatPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    IsrWithholdingPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    VatWithholdingPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    OrderTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Commission = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CommissionTax = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IsrWithheld = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    VatWithheld = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ExpectedSettlement = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RegisteredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformOrderDetails", x => x.Id);
                    table.CheckConstraint("CK_PlatformOrderDetails_Amounts", "[OrderTotal] > 0 AND [Commission] >= 0 AND [CommissionTax] >= 0 AND [IsrWithheld] >= 0 AND [VatWithheld] >= 0");
                    table.CheckConstraint("CK_PlatformOrderDetails_BusinessCollected", "[CollectedBy] = 0 OR ([IsrWithheld] = 0 AND [VatWithheld] = 0)");
                    table.CheckConstraint("CK_PlatformOrderDetails_Channel", "[Channel] IN (3, 4, 5)");
                    table.CheckConstraint("CK_PlatformOrderDetails_CollectedBy", "[CollectedBy] IN (0, 1)");
                    table.CheckConstraint("CK_PlatformOrderDetails_CommissionBase", "[CommissionBase] IN (0, 1)");
                    table.CheckConstraint("CK_PlatformOrderDetails_Number", "LEN([ExternalOrderNumber]) > 0");
                    table.CheckConstraint("CK_PlatformOrderDetails_Percents", "[CommissionPercent] BETWEEN 0 AND 100 AND [CommissionVatPercent] BETWEEN 0 AND 100 AND [IsrWithholdingPercent] BETWEEN 0 AND 100 AND [VatWithholdingPercent] BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_PlatformOrderDetails_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryPlatformSettings_TenantId_Channel",
                table: "DeliveryPlatformSettings",
                columns: new[] { "TenantId", "Channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformOrderDetails_OrderId",
                table: "PlatformOrderDetails",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformOrderDetails_TenantId_Channel_ExternalOrderNumber_RegisteredAt",
                table: "PlatformOrderDetails",
                columns: new[] { "TenantId", "Channel", "ExternalOrderNumber", "RegisteredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformOrderDetails_TenantId_RegisteredAt",
                table: "PlatformOrderDetails",
                columns: new[] { "TenantId", "RegisteredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // An order of a delivery platform (channels 3 to 5) or a «Plataforma» payment (method 5) would lose its
            // folio, commission and expected settlement, and the previous code does not know that channel or method
            // (it would take the refund of a void out of a drawer). With them there is only roll-forward. The guard
            // only names columns that exist before and after this migration, so the batch compiles when run again.
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM [Orders] WHERE [Channel] IN (3, 4, 5))
   OR EXISTS (SELECT 1 FROM [OrderPayments] WHERE [Method] = 5)
   OR EXISTS (SELECT 1 FROM [OrderRefunds] WHERE [Method] = 5)
   OR EXISTS (SELECT 1 FROM [CashTransactions] WHERE [PaymentMethod] = 5)
    THROW 50008, N'There are delivery platform orders or payments; rolling back would lose their folio, commission and expected settlement, and the previous code does not know their channel or payment method.', 1;
");

            migrationBuilder.DropTable(
                name: "PlatformOrderDetails");

            migrationBuilder.DropTable(
                name: "DeliveryPlatformSettings");

            migrationBuilder.DropColumn(
                name: "PlatformAmount",
                table: "DailySalesSnapshots");
        }
    }
}
