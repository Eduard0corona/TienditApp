using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppWebhookUniqueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The settings UI submits "" when no phone number ID is configured, so several
            // tenants can hold an empty value. Blank means "not configured": store NULL so the
            // filtered unique index below only covers real phone number IDs.
            migrationBuilder.Sql(
                "UPDATE [WhatsAppConfigs] SET [PhoneNumberId] = NULL WHERE LTRIM(RTRIM([PhoneNumberId])) = '';");

            // Real duplicates (two tenants with the same phone number ID, or one message stored
            // twice by a concurrent retry) make CreateIndex fail and roll the migration back on
            // purpose: they need a human decision, not an automatic delete. Detect them first with:
            //   SELECT PhoneNumberId, COUNT(*) FROM WhatsAppConfigs WHERE PhoneNumberId IS NOT NULL GROUP BY PhoneNumberId HAVING COUNT(*) > 1;
            //   SELECT ExternalMessageId, COUNT(*) FROM WhatsAppMessages WHERE ExternalMessageId IS NOT NULL GROUP BY ExternalMessageId HAVING COUNT(*) > 1;
            migrationBuilder.DropIndex(
                name: "IX_WhatsAppMessages_ExternalMessageId",
                table: "WhatsAppMessages");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_ExternalMessageId",
                table: "WhatsAppMessages",
                column: "ExternalMessageId",
                unique: true,
                filter: "[ExternalMessageId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppConfigs_PhoneNumberId",
                table: "WhatsAppConfigs",
                column: "PhoneNumberId",
                unique: true,
                filter: "[PhoneNumberId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Phone number IDs normalized from "" to NULL in Up are not restored:
            // both values mean "not configured" for the application.
            migrationBuilder.DropIndex(
                name: "IX_WhatsAppMessages_ExternalMessageId",
                table: "WhatsAppMessages");

            migrationBuilder.DropIndex(
                name: "IX_WhatsAppConfigs_PhoneNumberId",
                table: "WhatsAppConfigs");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_ExternalMessageId",
                table: "WhatsAppMessages",
                column: "ExternalMessageId");
        }
    }
}
