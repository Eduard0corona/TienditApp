using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The credit notes of the refunds of the sales a global invoice covers (Fase 28I, ADR 0054): a credit note credits an
    /// order's invoice or a global invoice, never both, and a global invoice's only for a refund, in one exhibition.
    /// </summary>
    public partial class AddGlobalInvoiceCreditNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "OrderInvoiceId",
                table: "CreditNotes",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "GlobalInvoiceId",
                table: "CreditNotes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_GlobalInvoiceId",
                table: "CreditNotes",
                column: "GlobalInvoiceId",
                filter: "[GlobalInvoiceId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreditNotes_Invoice",
                table: "CreditNotes",
                sql: "([OrderInvoiceId] IS NOT NULL AND [GlobalInvoiceId] IS NULL) OR ([OrderInvoiceId] IS NULL AND [GlobalInvoiceId] IS NOT NULL AND [OrderRefundId] IS NOT NULL AND [AfterInstallment] = 0)");

            migrationBuilder.AddForeignKey(
                name: "FK_CreditNotes_GlobalInvoices_GlobalInvoiceId",
                table: "CreditNotes",
                column: "GlobalInvoiceId",
                principalTable: "GlobalInvoices",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A credit note of a global invoice has no order's invoice to keep: going back would have to delete it, and it
            // stands with the SAT (or did). Stop.
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [CreditNotes] WHERE [GlobalInvoiceId] IS NOT NULL)
    THROW 50044, N''A credit note of a global invoice exists already; rolling back would lose it. Roll forward instead.'', 1;');
");

            migrationBuilder.DropForeignKey(
                name: "FK_CreditNotes_GlobalInvoices_GlobalInvoiceId",
                table: "CreditNotes");

            migrationBuilder.DropIndex(
                name: "IX_CreditNotes_GlobalInvoiceId",
                table: "CreditNotes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CreditNotes_Invoice",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "GlobalInvoiceId",
                table: "CreditNotes");

            migrationBuilder.AlterColumn<Guid>(
                name: "OrderInvoiceId",
                table: "CreditNotes",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
