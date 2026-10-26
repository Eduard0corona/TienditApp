using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSelfInvoicing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "SelfInvoiceKey",
                table: "InvoicingProfiles",
                type: "varbinary(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SelfInvoicing",
                table: "InvoicingProfiles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "CK_InvoicingProfiles_SelfInvoicing",
                table: "InvoicingProfiles",
                sql: "([SelfInvoicing] = 0 OR [SelfInvoiceKey] IS NOT NULL) AND ([SelfInvoiceKey] IS NULL OR DATALENGTH([SelfInvoiceKey]) = 32)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Without its key a business's printed tickets would stop taking their codes, even after rolling forward: stop.
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [InvoicingProfiles] WHERE [SelfInvoiceKey] IS NOT NULL)
    THROW 50042, N''A business has the key of its self-invoicing codes already; rolling back would lose it. Roll forward instead.'', 1;');
");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InvoicingProfiles_SelfInvoicing",
                table: "InvoicingProfiles");

            migrationBuilder.DropColumn(
                name: "SelfInvoiceKey",
                table: "InvoicingProfiles");

            migrationBuilder.DropColumn(
                name: "SelfInvoicing",
                table: "InvoicingProfiles");
        }
    }
}
