using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCfdiEmails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReceiverEmail",
                table: "OrderInvoices",
                type: "nvarchar(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceEmail",
                table: "Customers",
                type: "nvarchar(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CfdiEmails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Document = table.Column<int>(type: "int", nullable: false),
                    CfdiId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    To = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FailedAttempts = table.Column<int>(type: "int", nullable: false),
                    LastFailedAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SentWithoutPdf = table.Column<bool>(type: "bit", nullable: false),
                    FailedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Failure = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CfdiEmails", x => x.Id);
                    table.CheckConstraint("CK_CfdiEmails_Attempts", "([FailedAttempts] = 0 AND [LastFailedAttemptAt] IS NULL) OR ([FailedAttempts] > 0 AND [LastFailedAttemptAt] IS NOT NULL)");
                    table.CheckConstraint("CK_CfdiEmails_Document", "[Document] IN (0, 1, 2)");
                    table.CheckConstraint("CK_CfdiEmails_Invoice", "[Document] <> 0 OR [CfdiId] = [OrderInvoiceId]");
                    table.CheckConstraint("CK_CfdiEmails_Outcome", "([SentAt] IS NULL OR [FailedAt] IS NULL) AND (([FailedAt] IS NULL AND [Failure] IS NULL) OR ([FailedAt] IS NOT NULL AND [Failure] IS NOT NULL AND [Failure] IN (0, 1, 2))) AND ([SentWithoutPdf] = 0 OR [SentAt] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_CfdiEmails_OrderInvoices_OrderInvoiceId",
                        column: x => x.OrderInvoiceId,
                        principalTable: "OrderInvoices",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CfdiEmails_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Customers_InvoiceEmail",
                table: "Customers",
                sql: "[InvoiceEmail] IS NULL OR [FiscalData_Rfc] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CfdiEmails_OrderInvoiceId",
                table: "CfdiEmails",
                column: "OrderInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_CfdiEmails_TenantId_CfdiId_RequestedAt",
                table: "CfdiEmails",
                columns: new[] { "TenantId", "CfdiId", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CfdiEmails_TenantId_RequestedAt",
                table: "CfdiEmails",
                columns: new[] { "TenantId", "RequestedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The record of the emails, and the addresses the businesses typed, would be lost: stop instead.
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [CfdiEmails])
    OR EXISTS (SELECT 1 FROM [OrderInvoices] WHERE [ReceiverEmail] IS NOT NULL)
    OR EXISTS (SELECT 1 FROM [Customers] WHERE [InvoiceEmail] IS NOT NULL)
    THROW 50041, N''Invoice emails are recorded already; rolling back would lose them. Roll forward instead.'', 1;');
");

            migrationBuilder.DropTable(
                name: "CfdiEmails");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Customers_InvoiceEmail",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "ReceiverEmail",
                table: "OrderInvoices");

            migrationBuilder.DropColumn(
                name: "InvoiceEmail",
                table: "Customers");
        }
    }
}
