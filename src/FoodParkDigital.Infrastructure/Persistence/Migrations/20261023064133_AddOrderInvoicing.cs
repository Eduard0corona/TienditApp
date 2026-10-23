using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderInvoicing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FiscalData_CfdiUse",
                table: "Customers",
                type: "varchar(4)",
                unicode: false,
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FiscalData_FiscalRegime",
                table: "Customers",
                type: "varchar(3)",
                unicode: false,
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FiscalData_Name",
                table: "Customers",
                type: "nvarchar(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FiscalData_PostalCode",
                table: "Customers",
                type: "varchar(5)",
                unicode: false,
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FiscalData_Rfc",
                table: "Customers",
                type: "nvarchar(13)",
                maxLength: 13,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InvoicingProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Serie = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    CertificateNumber = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    CertificateRfc = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: true),
                    CertificateValidFrom = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CertificateValidTo = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CertificateUploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CertificateUploadedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoicingProfiles", x => x.Id);
                    table.CheckConstraint("CK_InvoicingProfiles_Certificate", "([CertificateNumber] IS NULL AND [CertificateRfc] IS NULL AND [CertificateValidFrom] IS NULL AND [CertificateValidTo] IS NULL AND [CertificateUploadedAt] IS NULL AND [CertificateUploadedByUserId] IS NULL) OR ([CertificateNumber] IS NOT NULL AND [CertificateRfc] IS NOT NULL AND [CertificateValidFrom] IS NOT NULL AND [CertificateValidTo] > [CertificateValidFrom] AND [CertificateUploadedAt] IS NOT NULL AND [CertificateUploadedByUserId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_InvoicingProfiles_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Serie = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    Folio = table.Column<long>(type: "bigint", nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Receiver_Rfc = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: false),
                    Receiver_Name = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    Receiver_PostalCode = table.Column<string>(type: "varchar(5)", unicode: false, maxLength: 5, nullable: false),
                    Receiver_FiscalRegime = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false),
                    Receiver_CfdiUse = table.Column<string>(type: "varchar(4)", unicode: false, maxLength: 4, nullable: false),
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
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderInvoices", x => x.Id);
                    table.CheckConstraint("CK_OrderInvoices_Amounts", "[Total] > 0 AND [SubTotal] >= 0 AND [TaxTotal] >= 0 AND [SubTotal] + [TaxTotal] = [Total]");
                    table.CheckConstraint("CK_OrderInvoices_Cancellation", "([Status] IN (0, 2) AND [CancellationMotive] IS NULL AND [CancellationRequestedAt] IS NULL AND [CancellationRequestedByUserId] IS NULL AND [CancelledAt] IS NULL) OR ([Status] = 1 AND [CancelledAt] IS NULL) OR ([Status] IN (3, 4) AND [CancellationMotive] IS NOT NULL AND [CancellationRequestedAt] IS NOT NULL AND [CancellationRequestedByUserId] IS NOT NULL AND (([Status] = 3 AND [CancelledAt] IS NULL) OR ([Status] = 4 AND [CancelledAt] IS NOT NULL)))");
                    table.CheckConstraint("CK_OrderInvoices_CancellationMotive", "[CancellationMotive] IS NULL OR [CancellationMotive] IN (2, 3)");
                    table.CheckConstraint("CK_OrderInvoices_Folio", "[Folio] > 0");
                    table.CheckConstraint("CK_OrderInvoices_PaymentMethod", "[PaymentMethod] = 'PUE'");
                    table.CheckConstraint("CK_OrderInvoices_Refused", "([Status] = 2 AND [RefusedAt] IS NOT NULL AND [FailureMessage] IS NOT NULL) OR ([Status] <> 2 AND [RefusedAt] IS NULL)");
                    table.CheckConstraint("CK_OrderInvoices_Stamp", "([Status] IN (0, 2) AND [Uuid] IS NULL AND [ProviderInvoiceId] IS NULL AND [StampedAt] IS NULL AND [XmlFileKey] IS NULL AND [PdfFileKey] IS NULL) OR ([Status] IN (1, 3, 4) AND [Uuid] IS NOT NULL AND [ProviderInvoiceId] IS NOT NULL AND [StampedAt] IS NOT NULL AND [XmlFileKey] IS NOT NULL)");
                    table.CheckConstraint("CK_OrderInvoices_Status", "[Status] IN (0, 1, 2, 3, 4)");
                    table.ForeignKey(
                        name: "FK_OrderInvoices_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrderInvoices_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Customers_FiscalData",
                table: "Customers",
                sql: "([FiscalData_Rfc] IS NULL AND [FiscalData_Name] IS NULL AND [FiscalData_PostalCode] IS NULL AND [FiscalData_FiscalRegime] IS NULL AND [FiscalData_CfdiUse] IS NULL) OR ([IsPublicGeneral] = 0 AND [FiscalData_Rfc] IS NOT NULL AND [FiscalData_Name] IS NOT NULL AND [FiscalData_PostalCode] IS NOT NULL AND [FiscalData_FiscalRegime] IS NOT NULL AND [FiscalData_CfdiUse] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_InvoicingProfiles_CertificateNumber",
                table: "InvoicingProfiles",
                column: "CertificateNumber",
                unique: true,
                filter: "[CertificateNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InvoicingProfiles_TenantId",
                table: "InvoicingProfiles",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderInvoices_IdempotencyKey",
                table: "OrderInvoices",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderInvoices_OrderId",
                table: "OrderInvoices",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderInvoices_Status_RequestedAt",
                table: "OrderInvoices",
                columns: new[] { "Status", "RequestedAt" },
                filter: "[Status] IN (0, 3)");

            migrationBuilder.CreateIndex(
                name: "IX_OrderInvoices_TenantId_OrderId_Live",
                table: "OrderInvoices",
                columns: new[] { "TenantId", "OrderId" },
                unique: true,
                filter: "[Status] IN (0, 1, 3)");

            migrationBuilder.CreateIndex(
                name: "IX_OrderInvoices_TenantId_Serie_Folio",
                table: "OrderInvoices",
                columns: new[] { "TenantId", "Serie", "Folio" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderInvoices_TenantId_Uuid",
                table: "OrderInvoices",
                columns: new[] { "TenantId", "Uuid" },
                unique: true,
                filter: "[Uuid] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Invoices stamped (or asked for), a CSD at the PAC or a customer's fiscal data would be lost: stop instead.
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [OrderInvoices])
    OR EXISTS (SELECT 1 FROM [InvoicingProfiles] WHERE [CertificateNumber] IS NOT NULL)
    OR EXISTS (SELECT 1 FROM [Customers] WHERE [FiscalData_Rfc] IS NOT NULL)
    THROW 50037, N''Invoices, a CSD or customers'''' fiscal data are recorded already; rolling back would delete them. Roll forward instead.'', 1;');
");

            migrationBuilder.DropTable(
                name: "InvoicingProfiles");

            migrationBuilder.DropTable(
                name: "OrderInvoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Customers_FiscalData",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "FiscalData_CfdiUse",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "FiscalData_FiscalRegime",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "FiscalData_Name",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "FiscalData_PostalCode",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "FiscalData_Rfc",
                table: "Customers");
        }
    }
}
