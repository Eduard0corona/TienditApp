using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCfdiWhatsApp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CfdiDocument",
                table: "OutgoingMessages",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CfdiId",
                table: "OutgoingMessages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LinkToken",
                table: "OutgoingMessages",
                type: "varchar(43)",
                unicode: false,
                maxLength: 43,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OrderInvoiceId",
                table: "OutgoingMessages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestedByUserId",
                table: "OutgoingMessages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceiverWhatsApp",
                table: "OrderInvoices",
                type: "varchar(20)",
                unicode: false,
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WhatsAppTemplateId",
                table: "InvoicingProfiles",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingMessages_LinkToken",
                table: "OutgoingMessages",
                column: "LinkToken",
                unique: true,
                filter: "[LinkToken] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingMessages_OrderInvoiceId",
                table: "OutgoingMessages",
                column: "OrderInvoiceId",
                filter: "[OrderInvoiceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingMessages_TenantId_CfdiId_CreatedAt",
                table: "OutgoingMessages",
                columns: new[] { "TenantId", "CfdiId", "CreatedAt" },
                filter: "[CfdiId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OutgoingMessages_Cfdi",
                table: "OutgoingMessages",
                sql: "([Purpose] = 5 AND [CfdiDocument] IS NOT NULL AND [CfdiId] IS NOT NULL AND [OrderInvoiceId] IS NOT NULL AND [LinkToken] IS NOT NULL) OR ([Purpose] <> 5 AND [CfdiDocument] IS NULL AND [CfdiId] IS NULL AND [OrderInvoiceId] IS NULL AND [RequestedByUserId] IS NULL AND [LinkToken] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OutgoingMessages_CfdiDocument",
                table: "OutgoingMessages",
                sql: "[CfdiDocument] IS NULL OR [CfdiDocument] IN (0, 1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OutgoingMessages_CfdiInvoice",
                table: "OutgoingMessages",
                sql: "[CfdiDocument] IS NULL OR [CfdiDocument] <> 0 OR [CfdiId] = [OrderInvoiceId]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OutgoingMessages_LinkToken",
                table: "OutgoingMessages",
                sql: "[LinkToken] IS NULL OR LEN([LinkToken]) = 43");

            migrationBuilder.AddForeignKey(
                name: "FK_OutgoingMessages_OrderInvoices_OrderInvoiceId",
                table: "OutgoingMessages",
                column: "OrderInvoiceId",
                principalTable: "OrderInvoices",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Going back would delete the WhatsApp messages of the CFDI (their history and their links) and the numbers and
            // template the businesses chose: stop.
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [OutgoingMessages] WHERE [Purpose] = 5 OR [CfdiId] IS NOT NULL)
    OR EXISTS (SELECT 1 FROM [OrderInvoices] WHERE [ReceiverWhatsApp] IS NOT NULL)
    OR EXISTS (SELECT 1 FROM [InvoicingProfiles] WHERE [WhatsAppTemplateId] IS NOT NULL)
    THROW 50046, N''Invoice WhatsApp messages are recorded already; rolling back would lose them. Roll forward instead.'', 1;');
");

            migrationBuilder.DropForeignKey(
                name: "FK_OutgoingMessages_OrderInvoices_OrderInvoiceId",
                table: "OutgoingMessages");

            migrationBuilder.DropIndex(
                name: "IX_OutgoingMessages_LinkToken",
                table: "OutgoingMessages");

            migrationBuilder.DropIndex(
                name: "IX_OutgoingMessages_OrderInvoiceId",
                table: "OutgoingMessages");

            migrationBuilder.DropIndex(
                name: "IX_OutgoingMessages_TenantId_CfdiId_CreatedAt",
                table: "OutgoingMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OutgoingMessages_Cfdi",
                table: "OutgoingMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OutgoingMessages_CfdiDocument",
                table: "OutgoingMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OutgoingMessages_CfdiInvoice",
                table: "OutgoingMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OutgoingMessages_LinkToken",
                table: "OutgoingMessages");

            migrationBuilder.DropColumn(
                name: "CfdiDocument",
                table: "OutgoingMessages");

            migrationBuilder.DropColumn(
                name: "CfdiId",
                table: "OutgoingMessages");

            migrationBuilder.DropColumn(
                name: "LinkToken",
                table: "OutgoingMessages");

            migrationBuilder.DropColumn(
                name: "OrderInvoiceId",
                table: "OutgoingMessages");

            migrationBuilder.DropColumn(
                name: "RequestedByUserId",
                table: "OutgoingMessages");

            migrationBuilder.DropColumn(
                name: "ReceiverWhatsApp",
                table: "OrderInvoices");

            migrationBuilder.DropColumn(
                name: "WhatsAppTemplateId",
                table: "InvoicingProfiles");
        }
    }
}
