using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGlobalInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GlobalInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Periodicity = table.Column<int>(type: "int", nullable: false),
                    PeriodFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodTo = table.Column<DateOnly>(type: "date", nullable: false),
                    Months = table.Column<string>(type: "char(2)", unicode: false, fixedLength: true, maxLength: 2, nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    OrderCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Serie = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    Folio = table.Column<long>(type: "bigint", nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PaymentForm = table.Column<string>(type: "varchar(2)", unicode: false, maxLength: 2, nullable: false),
                    PaymentMethod = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false),
                    Document = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SubTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Uuid = table.Column<string>(type: "varchar(36)", unicode: false, maxLength: 36, nullable: true),
                    ProviderInvoiceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    StampedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    XmlFileKey = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    PdfFileKey = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    FailureMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RefusedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancellationMotive = table.Column<int>(type: "int", nullable: true),
                    CancellationRequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CancellationRequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalInvoices", x => x.Id);
                    table.CheckConstraint("CK_GlobalInvoices_Amounts", "[Total] > 0 AND [SubTotal] >= 0 AND [TaxTotal] >= 0 AND [SubTotal] + [TaxTotal] = [Total]");
                    table.CheckConstraint("CK_GlobalInvoices_Cancellation", "([Status] IN (0, 2) AND [CancellationMotive] IS NULL AND [CancellationRequestedAt] IS NULL AND [CancellationRequestedByUserId] IS NULL AND [CancelledAt] IS NULL) OR ([Status] = 1 AND [CancelledAt] IS NULL) OR ([Status] IN (3, 4) AND [CancellationMotive] IS NOT NULL AND [CancellationRequestedAt] IS NOT NULL AND [CancellationRequestedByUserId] IS NOT NULL AND (([Status] = 3 AND [CancelledAt] IS NULL) OR ([Status] = 4 AND [CancelledAt] IS NOT NULL)))");
                    table.CheckConstraint("CK_GlobalInvoices_CancellationMotive", "[CancellationMotive] IS NULL OR [CancellationMotive] IN (2, 4)");
                    table.CheckConstraint("CK_GlobalInvoices_Folio", "[Folio] > 0");
                    table.CheckConstraint("CK_GlobalInvoices_PaymentMethod", "[PaymentMethod] = 'PUE'");
                    table.CheckConstraint("CK_GlobalInvoices_Period", "[Periodicity] IN (1, 2, 3, 4, 5) AND [PeriodFrom] <= [PeriodTo] AND [Year] = YEAR([PeriodFrom]) AND [Months] IN ('01', '02', '03', '04', '05', '06', '07', '08', '09', '10', '11', '12', '13', '14', '15', '16', '17', '18') AND [OrderCount] > 0");
                    table.CheckConstraint("CK_GlobalInvoices_Refused", "([Status] = 2 AND [RefusedAt] IS NOT NULL AND [FailureMessage] IS NOT NULL) OR ([Status] <> 2 AND [RefusedAt] IS NULL)");
                    table.CheckConstraint("CK_GlobalInvoices_Stamp", "([Status] IN (0, 2) AND [Uuid] IS NULL AND [ProviderInvoiceId] IS NULL AND [StampedAt] IS NULL AND [XmlFileKey] IS NULL AND [PdfFileKey] IS NULL) OR ([Status] IN (1, 3, 4) AND [Uuid] IS NOT NULL AND [ProviderInvoiceId] IS NOT NULL AND [StampedAt] IS NOT NULL AND [XmlFileKey] IS NOT NULL)");
                    table.CheckConstraint("CK_GlobalInvoices_Status", "[Status] IN (0, 1, 2, 3, 4)");
                    table.ForeignKey(
                        name: "FK_GlobalInvoices_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GlobalInvoiceOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GlobalInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Released = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalInvoiceOrders", x => x.Id);
                    table.CheckConstraint("CK_GlobalInvoiceOrders_Total", "[Total] > 0");
                    table.ForeignKey(
                        name: "FK_GlobalInvoiceOrders_GlobalInvoices_GlobalInvoiceId",
                        column: x => x.GlobalInvoiceId,
                        principalTable: "GlobalInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GlobalInvoiceOrders_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_GlobalInvoiceOrders_GlobalInvoiceId",
                table: "GlobalInvoiceOrders",
                column: "GlobalInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_GlobalInvoiceOrders_OrderId",
                table: "GlobalInvoiceOrders",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_GlobalInvoiceOrders_TenantId_OrderId_Live",
                table: "GlobalInvoiceOrders",
                columns: new[] { "TenantId", "OrderId" },
                unique: true,
                filter: "[Released] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_GlobalInvoices_IdempotencyKey",
                table: "GlobalInvoices",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GlobalInvoices_Status_RequestedAt",
                table: "GlobalInvoices",
                columns: new[] { "Status", "RequestedAt" },
                filter: "[Status] IN (0, 3)");

            migrationBuilder.CreateIndex(
                name: "IX_GlobalInvoices_TenantId_PeriodFrom",
                table: "GlobalInvoices",
                columns: new[] { "TenantId", "PeriodFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_GlobalInvoices_TenantId_Serie_Folio",
                table: "GlobalInvoices",
                columns: new[] { "TenantId", "Serie", "Folio" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GlobalInvoices_TenantId_Uuid",
                table: "GlobalInvoices",
                columns: new[] { "TenantId", "Uuid" },
                unique: true,
                filter: "[Uuid] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A global invoice (stamped, asked for or refused) would be lost, and its sales would look uninvoiced: stop instead.
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [GlobalInvoices])
    THROW 50038, N''Global invoices are recorded already; rolling back would delete them. Roll forward instead.'', 1;');
");

            migrationBuilder.DropTable(
                name: "GlobalInvoiceOrders");

            migrationBuilder.DropTable(
                name: "GlobalInvoices");
        }
    }
}
