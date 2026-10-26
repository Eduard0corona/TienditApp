using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIssuedCfdiDateIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_PaymentComplements_TenantId_IssuedAt",
                table: "PaymentComplements",
                columns: new[] { "TenantId", "IssuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderInvoices_TenantId_IssuedAt",
                table: "OrderInvoices",
                columns: new[] { "TenantId", "IssuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_GlobalInvoices_TenantId_IssuedAt",
                table: "GlobalInvoices",
                columns: new[] { "TenantId", "IssuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_TenantId_IssuedAt",
                table: "CreditNotes",
                columns: new[] { "TenantId", "IssuedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentComplements_TenantId_IssuedAt",
                table: "PaymentComplements");

            migrationBuilder.DropIndex(
                name: "IX_OrderInvoices_TenantId_IssuedAt",
                table: "OrderInvoices");

            migrationBuilder.DropIndex(
                name: "IX_GlobalInvoices_TenantId_IssuedAt",
                table: "GlobalInvoices");

            migrationBuilder.DropIndex(
                name: "IX_CreditNotes_TenantId_IssuedAt",
                table: "CreditNotes");
        }
    }
}
