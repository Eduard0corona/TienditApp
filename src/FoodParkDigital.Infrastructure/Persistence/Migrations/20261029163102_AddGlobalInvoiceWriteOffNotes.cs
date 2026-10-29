using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The credit notes of the write-offs of the sales a global invoice covers (Fase 28J, ADR 0055): a global invoice's credit
    /// note may credit a refund or a write-off of one of its sales, still in one exhibition.
    /// </summary>
    public partial class AddGlobalInvoiceWriteOffNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CreditNotes_Invoice",
                table: "CreditNotes");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreditNotes_Invoice",
                table: "CreditNotes",
                sql: "([OrderInvoiceId] IS NOT NULL AND [GlobalInvoiceId] IS NULL) OR ([OrderInvoiceId] IS NULL AND [GlobalInvoiceId] IS NOT NULL AND [AfterInstallment] = 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The restriction it goes back to refuses a global invoice's credit note of a write-off: going back would have to
            // delete it, and it stands with the SAT (or did). Stop.
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [CreditNotes] WHERE [GlobalInvoiceId] IS NOT NULL AND [OrderWriteOffId] IS NOT NULL)
    THROW 50045, N''A credit note of a write-off of a global invoice exists already; rolling back would lose it. Roll forward instead.'', 1;');
");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CreditNotes_Invoice",
                table: "CreditNotes");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreditNotes_Invoice",
                table: "CreditNotes",
                sql: "([OrderInvoiceId] IS NOT NULL AND [GlobalInvoiceId] IS NULL) OR ([OrderInvoiceId] IS NULL AND [GlobalInvoiceId] IS NOT NULL AND [OrderRefundId] IS NOT NULL AND [AfterInstallment] = 0)");
        }
    }
}
