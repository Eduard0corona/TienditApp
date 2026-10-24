using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentComplements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderInvoices_PaymentMethod",
                table: "OrderInvoices");

            migrationBuilder.AddColumn<string>(
                name: "PaymentSerie",
                table: "InvoicingProfiles",
                type: "varchar(10)",
                unicode: false,
                maxLength: 10,
                nullable: false,
                defaultValue: "P");

            migrationBuilder.CreateTable(
                name: "PaymentComplements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderPaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaidAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PaymentForm = table.Column<string>(type: "char(2)", unicode: false, fixedLength: true, maxLength: 2, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Installment = table.Column<int>(type: "int", nullable: false),
                    PreviousBalance = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RemainingBalance = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    Document = table.Column<string>(type: "nvarchar(max)", nullable: false),
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
                    table.PrimaryKey("PK_PaymentComplements", x => x.Id);
                    table.CheckConstraint("CK_PaymentComplements_Amounts", "[Amount] > 0 AND [PreviousBalance] > 0 AND [Amount] <= [PreviousBalance] AND [RemainingBalance] = [PreviousBalance] - [Amount]");
                    table.CheckConstraint("CK_PaymentComplements_Cancellation", "([Status] IN (0, 2) AND [CancellationMotive] IS NULL AND [CancellationRequestedAt] IS NULL AND [CancellationRequestedByUserId] IS NULL AND [CancelledAt] IS NULL) OR ([Status] = 1 AND [CancelledAt] IS NULL) OR ([Status] IN (3, 4) AND [CancellationMotive] IS NOT NULL AND [CancellationRequestedAt] IS NOT NULL AND [CancellationRequestedByUserId] IS NOT NULL AND (([Status] = 3 AND [CancelledAt] IS NULL) OR ([Status] = 4 AND [CancelledAt] IS NOT NULL)))");
                    table.CheckConstraint("CK_PaymentComplements_CancellationMotive", "[CancellationMotive] IS NULL OR [CancellationMotive] IN (2, 3)");
                    table.CheckConstraint("CK_PaymentComplements_Folio", "[Folio] > 0");
                    table.CheckConstraint("CK_PaymentComplements_Installment", "[Installment] BETWEEN 1 AND 999");
                    table.CheckConstraint("CK_PaymentComplements_PaymentForm", "[PaymentForm] NOT IN ('99', '30')");
                    table.CheckConstraint("CK_PaymentComplements_Refused", "([Status] = 2 AND [RefusedAt] IS NOT NULL AND [FailureMessage] IS NOT NULL) OR ([Status] <> 2 AND [RefusedAt] IS NULL)");
                    table.CheckConstraint("CK_PaymentComplements_Stamp", "([Status] IN (0, 2) AND [Uuid] IS NULL AND [ProviderInvoiceId] IS NULL AND [StampedAt] IS NULL AND [XmlFileKey] IS NULL AND [PdfFileKey] IS NULL) OR ([Status] IN (1, 3, 4) AND [Uuid] IS NOT NULL AND [ProviderInvoiceId] IS NOT NULL AND [StampedAt] IS NOT NULL AND [XmlFileKey] IS NOT NULL)");
                    table.CheckConstraint("CK_PaymentComplements_Status", "[Status] IN (0, 1, 2, 3, 4)");
                    table.ForeignKey(
                        name: "FK_PaymentComplements_OrderInvoices_OrderInvoiceId",
                        column: x => x.OrderInvoiceId,
                        principalTable: "OrderInvoices",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PaymentComplements_OrderPayments_OrderPaymentId",
                        column: x => x.OrderPaymentId,
                        principalTable: "OrderPayments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PaymentComplements_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PaymentComplements_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderInvoices_PaymentMethod",
                table: "OrderInvoices",
                sql: "([PaymentMethod] = 'PUE' AND [PaymentForm] <> '99') OR ([PaymentMethod] = 'PPD' AND [PaymentForm] = '99')");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentComplements_IdempotencyKey",
                table: "PaymentComplements",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentComplements_OrderId",
                table: "PaymentComplements",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentComplements_OrderInvoiceId_Installment_Live",
                table: "PaymentComplements",
                columns: new[] { "OrderInvoiceId", "Installment" },
                unique: true,
                filter: "[Status] IN (0, 1, 3)");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentComplements_OrderPaymentId",
                table: "PaymentComplements",
                column: "OrderPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentComplements_Status_RequestedAt",
                table: "PaymentComplements",
                columns: new[] { "Status", "RequestedAt" },
                filter: "[Status] IN (0, 3)");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentComplements_TenantId_OrderPaymentId_Live",
                table: "PaymentComplements",
                columns: new[] { "TenantId", "OrderPaymentId" },
                unique: true,
                filter: "[Status] IN (0, 1, 3)");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentComplements_TenantId_Serie_Folio",
                table: "PaymentComplements",
                columns: new[] { "TenantId", "Serie", "Folio" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentComplements_TenantId_Uuid",
                table: "PaymentComplements",
                columns: new[] { "TenantId", "Uuid" },
                unique: true,
                filter: "[Uuid] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A payment complement or an invoice in installments (stamped, asked for or refused) would be lost, or the old
            // check on the invoices would refuse it: stop instead.
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [PaymentComplements]) OR EXISTS (SELECT 1 FROM [OrderInvoices] WHERE [PaymentMethod] = ''PPD'')
    THROW 50039, N''Payment complements or invoices in installments are recorded already; rolling back would lose them. Roll forward instead.'', 1;');
");

            migrationBuilder.DropTable(
                name: "PaymentComplements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderInvoices_PaymentMethod",
                table: "OrderInvoices");

            migrationBuilder.DropColumn(
                name: "PaymentSerie",
                table: "InvoicingProfiles");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderInvoices_PaymentMethod",
                table: "OrderInvoices",
                sql: "[PaymentMethod] = 'PUE'");
        }
    }
}
