using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceSubstitution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrderInvoices_TenantId_OrderId_Live",
                table: "OrderInvoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderInvoices_CancellationMotive",
                table: "OrderInvoices");

            migrationBuilder.AddColumn<Guid>(
                name: "ReplacedByInvoiceId",
                table: "OrderInvoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderInvoices_ReplacedByInvoiceId",
                table: "OrderInvoices",
                column: "ReplacedByInvoiceId",
                unique: true,
                filter: "[ReplacedByInvoiceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrderInvoices_TenantId_OrderId_Live",
                table: "OrderInvoices",
                columns: new[] { "TenantId", "OrderId" },
                unique: true,
                filter: "[Status] IN (0, 1, 3) AND [ReplacedByInvoiceId] IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderInvoices_CancellationMotive",
                table: "OrderInvoices",
                sql: "[CancellationMotive] IS NULL OR [CancellationMotive] IN (2, 3) OR ([CancellationMotive] = 1 AND [ReplacedByInvoiceId] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderInvoices_Replacement",
                table: "OrderInvoices",
                sql: "[ReplacedByInvoiceId] IS NULL OR ([ReplacedByInvoiceId] <> [Id] AND [Status] IN (1, 3, 4))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Without the column the relation between an invoice and the one that replaced it is lost, and an order whose
            // replaced invoice still stands would have two live ones the old index refuses: stop.
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [OrderInvoices] WHERE [ReplacedByInvoiceId] IS NOT NULL)
    THROW 50043, N''An invoice has been replaced by another already; rolling back would lose that relation. Roll forward instead.'', 1;');
");

            migrationBuilder.DropIndex(
                name: "IX_OrderInvoices_ReplacedByInvoiceId",
                table: "OrderInvoices");

            migrationBuilder.DropIndex(
                name: "IX_OrderInvoices_TenantId_OrderId_Live",
                table: "OrderInvoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderInvoices_CancellationMotive",
                table: "OrderInvoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderInvoices_Replacement",
                table: "OrderInvoices");

            migrationBuilder.DropColumn(
                name: "ReplacedByInvoiceId",
                table: "OrderInvoices");

            migrationBuilder.CreateIndex(
                name: "IX_OrderInvoices_TenantId_OrderId_Live",
                table: "OrderInvoices",
                columns: new[] { "TenantId", "OrderId" },
                unique: true,
                filter: "[Status] IN (0, 1, 3)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderInvoices_CancellationMotive",
                table: "OrderInvoices",
                sql: "[CancellationMotive] IS NULL OR [CancellationMotive] IN (2, 3)");
        }
    }
}
