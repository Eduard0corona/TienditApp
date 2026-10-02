using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppConversationVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SQL Server fills the rowversion of existing rows and ignores the default value below, so
            // no data changes. Apply it before deploying the code that uses the column: the current code
            // keeps working with it, while the new code fails against a database without it.
            migrationBuilder.AddColumn<byte[]>(
                name: "Version",
                table: "WhatsAppConversations",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Version",
                table: "WhatsAppConversations");
        }
    }
}
