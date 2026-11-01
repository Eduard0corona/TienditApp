using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQuoteDeliveries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Fase 29D (ADR 0053 point 16): the quotes go out from TienditApp. Only new columns and a new table; the rule of the
            // queue keeps every row the deployed version writes (none of them says who asked for it, nor carries a quote).
            migrationBuilder.DropCheckConstraint(
                name: "CK_OutgoingMessages_Cfdi",
                table: "OutgoingMessages");

            migrationBuilder.AddColumn<Guid>(
                name: "QuoteWhatsAppTemplateId",
                table: "TenantSettings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "QuoteId",
                table: "OutgoingMessages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "QuoteEmails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    To = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FailedAttempts = table.Column<int>(type: "int", nullable: false),
                    LastFailedAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FailedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Failure = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteEmails", x => x.Id);
                    table.CheckConstraint("CK_QuoteEmails_Attempts", "([FailedAttempts] = 0 AND [LastFailedAttemptAt] IS NULL) OR ([FailedAttempts] > 0 AND [LastFailedAttemptAt] IS NOT NULL)");
                    table.CheckConstraint("CK_QuoteEmails_Outcome", "([SentAt] IS NULL OR [FailedAt] IS NULL) AND (([FailedAt] IS NULL AND [Failure] IS NULL) OR ([FailedAt] IS NOT NULL AND [Failure] IS NOT NULL AND [Failure] IN (0, 1, 2, 3)))");
                    table.ForeignKey(
                        name: "FK_QuoteEmails_Quotes_QuoteId",
                        column: x => x.QuoteId,
                        principalTable: "Quotes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuoteEmails_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingMessages_QuoteId_CreatedAt",
                table: "OutgoingMessages",
                columns: new[] { "QuoteId", "CreatedAt" },
                filter: "[QuoteId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingMessages_Quotes_TenantId_CreatedAt",
                table: "OutgoingMessages",
                columns: new[] { "TenantId", "CreatedAt" },
                filter: "[QuoteId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OutgoingMessages_Cfdi",
                table: "OutgoingMessages",
                sql: "([Purpose] = 5 AND [CfdiDocument] IS NOT NULL AND [CfdiId] IS NOT NULL AND [OrderInvoiceId] IS NOT NULL AND [LinkToken] IS NOT NULL) OR ([Purpose] <> 5 AND [CfdiDocument] IS NULL AND [CfdiId] IS NULL AND [OrderInvoiceId] IS NULL AND [LinkToken] IS NULL AND ([RequestedByUserId] IS NULL OR [Purpose] = 6))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OutgoingMessages_Quote",
                table: "OutgoingMessages",
                sql: "([Purpose] = 6 AND [QuoteId] IS NOT NULL AND [RequestedByUserId] IS NOT NULL) OR ([Purpose] <> 6 AND [QuoteId] IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteEmails_QuoteId_RequestedAt",
                table: "QuoteEmails",
                columns: new[] { "QuoteId", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteEmails_TenantId_RequestedAt",
                table: "QuoteEmails",
                columns: new[] { "TenantId", "RequestedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_OutgoingMessages_Quotes_QuoteId",
                table: "OutgoingMessages",
                column: "QuoteId",
                principalTable: "Quotes",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rolling back loses the history of the quotes sent by email and by WhatsApp, and the template a business picked for
            // them; a message of a quote would also break the earlier rule of the queue. Once there is one of them, there is
            // only roll-forward. Through EXEC: on a second run of rollback.sql the table and the columns are gone, and a batch
            // that names them would not compile.
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [QuoteEmails])
    OR EXISTS (SELECT 1 FROM [OutgoingMessages] WHERE [QuoteId] IS NOT NULL OR [Purpose] = 6)
    OR EXISTS (SELECT 1 FROM [TenantSettings] WHERE [QuoteWhatsAppTemplateId] IS NOT NULL)
    THROW 50052, N''Businesses already sent quotes from TienditApp, or picked a WhatsApp template for them; rolling back would lose them. Roll forward instead.'', 1;');
");

            migrationBuilder.DropForeignKey(
                name: "FK_OutgoingMessages_Quotes_QuoteId",
                table: "OutgoingMessages");

            migrationBuilder.DropTable(
                name: "QuoteEmails");

            migrationBuilder.DropIndex(
                name: "IX_OutgoingMessages_QuoteId_CreatedAt",
                table: "OutgoingMessages");

            migrationBuilder.DropIndex(
                name: "IX_OutgoingMessages_Quotes_TenantId_CreatedAt",
                table: "OutgoingMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OutgoingMessages_Cfdi",
                table: "OutgoingMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OutgoingMessages_Quote",
                table: "OutgoingMessages");

            migrationBuilder.DropColumn(
                name: "QuoteWhatsAppTemplateId",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "QuoteId",
                table: "OutgoingMessages");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OutgoingMessages_Cfdi",
                table: "OutgoingMessages",
                sql: "([Purpose] = 5 AND [CfdiDocument] IS NOT NULL AND [CfdiId] IS NOT NULL AND [OrderInvoiceId] IS NOT NULL AND [LinkToken] IS NOT NULL) OR ([Purpose] <> 5 AND [CfdiDocument] IS NULL AND [CfdiId] IS NULL AND [OrderInvoiceId] IS NULL AND [RequestedByUserId] IS NULL AND [LinkToken] IS NULL)");
        }
    }
}
