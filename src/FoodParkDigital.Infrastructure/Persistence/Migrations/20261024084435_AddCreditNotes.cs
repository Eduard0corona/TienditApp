using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCreditNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreditNoteSerie",
                table: "InvoicingProfiles",
                type: "varchar(10)",
                unicode: false,
                maxLength: 10,
                nullable: false,
                defaultValue: "NC");

            migrationBuilder.CreateTable(
                name: "CreditNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderRefundId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OrderWriteOffId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PaymentForm = table.Column<string>(type: "char(2)", unicode: false, fixedLength: true, maxLength: 2, nullable: false),
                    SubTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AfterInstallment = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_CreditNotes", x => x.Id);
                    table.CheckConstraint("CK_CreditNotes_Adjustment", "([OrderRefundId] IS NOT NULL AND [OrderWriteOffId] IS NULL AND [PaymentForm] NOT IN ('99', '30', '15')) OR ([OrderRefundId] IS NULL AND [OrderWriteOffId] IS NOT NULL AND [PaymentForm] = '15')");
                    table.CheckConstraint("CK_CreditNotes_AfterInstallment", "[AfterInstallment] BETWEEN 0 AND 999");
                    table.CheckConstraint("CK_CreditNotes_Amounts", "[Total] > 0 AND [SubTotal] >= 0 AND [TaxTotal] >= 0 AND [SubTotal] + [TaxTotal] = [Total]");
                    table.CheckConstraint("CK_CreditNotes_Cancellation", "([Status] IN (0, 2) AND [CancellationMotive] IS NULL AND [CancellationRequestedAt] IS NULL AND [CancellationRequestedByUserId] IS NULL AND [CancelledAt] IS NULL) OR ([Status] = 1 AND [CancelledAt] IS NULL) OR ([Status] IN (3, 4) AND [CancellationMotive] IS NOT NULL AND [CancellationRequestedAt] IS NOT NULL AND [CancellationRequestedByUserId] IS NOT NULL AND (([Status] = 3 AND [CancelledAt] IS NULL) OR ([Status] = 4 AND [CancelledAt] IS NOT NULL)))");
                    table.CheckConstraint("CK_CreditNotes_CancellationMotive", "[CancellationMotive] IS NULL OR [CancellationMotive] IN (2, 3)");
                    table.CheckConstraint("CK_CreditNotes_Folio", "[Folio] > 0");
                    table.CheckConstraint("CK_CreditNotes_Refused", "([Status] = 2 AND [RefusedAt] IS NOT NULL AND [FailureMessage] IS NOT NULL) OR ([Status] <> 2 AND [RefusedAt] IS NULL)");
                    table.CheckConstraint("CK_CreditNotes_Stamp", "([Status] IN (0, 2) AND [Uuid] IS NULL AND [ProviderInvoiceId] IS NULL AND [StampedAt] IS NULL AND [XmlFileKey] IS NULL AND [PdfFileKey] IS NULL) OR ([Status] IN (1, 3, 4) AND [Uuid] IS NOT NULL AND [ProviderInvoiceId] IS NOT NULL AND [StampedAt] IS NOT NULL AND [XmlFileKey] IS NOT NULL)");
                    table.CheckConstraint("CK_CreditNotes_Status", "[Status] IN (0, 1, 2, 3, 4)");
                    table.ForeignKey(
                        name: "FK_CreditNotes_OrderInvoices_OrderInvoiceId",
                        column: x => x.OrderInvoiceId,
                        principalTable: "OrderInvoices",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CreditNotes_OrderRefunds_OrderRefundId",
                        column: x => x.OrderRefundId,
                        principalTable: "OrderRefunds",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CreditNotes_OrderWriteOffs_OrderWriteOffId",
                        column: x => x.OrderWriteOffId,
                        principalTable: "OrderWriteOffs",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CreditNotes_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CreditNotes_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_IdempotencyKey",
                table: "CreditNotes",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_OrderId",
                table: "CreditNotes",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_OrderInvoiceId",
                table: "CreditNotes",
                column: "OrderInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_OrderRefundId_Live",
                table: "CreditNotes",
                column: "OrderRefundId",
                unique: true,
                filter: "[OrderRefundId] IS NOT NULL AND [Status] IN (0, 1, 3)");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_OrderWriteOffId_Live",
                table: "CreditNotes",
                column: "OrderWriteOffId",
                unique: true,
                filter: "[OrderWriteOffId] IS NOT NULL AND [Status] IN (0, 1, 3)");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_Status_RequestedAt",
                table: "CreditNotes",
                columns: new[] { "Status", "RequestedAt" },
                filter: "[Status] IN (0, 3)");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_TenantId_Serie_Folio",
                table: "CreditNotes",
                columns: new[] { "TenantId", "Serie", "Folio" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_TenantId_Uuid",
                table: "CreditNotes",
                columns: new[] { "TenantId", "Uuid" },
                unique: true,
                filter: "[Uuid] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A credit note (stamped, asked for or refused) would be lost: stop instead.
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [CreditNotes])
    THROW 50040, N''Credit notes are recorded already; rolling back would lose them. Roll forward instead.'', 1;');
");

            migrationBuilder.DropTable(
                name: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "CreditNoteSerie",
                table: "InvoicingProfiles");
        }
    }
}
