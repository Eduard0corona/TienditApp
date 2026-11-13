using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppMessageMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MediaId",
                table: "WhatsAppMessages",
                type: "varchar(128)",
                unicode: false,
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MediaKind",
                table: "WhatsAppMessages",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MediaPath",
                table: "WhatsAppMessages",
                type: "varchar(300)",
                unicode: false,
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MediaState",
                table: "WhatsAppMessages",
                type: "int",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_WhatsAppMessages_Media",
                table: "WhatsAppMessages",
                sql: "([MediaKind] IS NULL AND [MediaId] IS NULL AND [MediaPath] IS NULL AND [MediaState] IS NULL) OR ([MediaKind] IN (1, 2) AND [MediaId] IS NOT NULL AND (([MediaState] IN (0, 2) AND [MediaPath] IS NULL) OR ([MediaState] = 1 AND [MediaPath] IS NOT NULL)))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Fase 31L (ADR 0092): rolling back would lose where the photos and audios the customers sent are kept. Once one
            // was saved, it stops before anything changes; query 4 of verify.sql counts them. Without any, it drops only what
            // Up added (the ones waiting or given up keep their marker as their text).
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [WhatsAppMessages] WITH (TABLOCKX, HOLDLOCK) WHERE [MediaPath] IS NOT NULL)
    THROW 50061, N''A WhatsApp message keeps a photo or an audio a customer sent; rolling back would lose where it is (query 4 of verify.sql).'', 1;');
");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WhatsAppMessages_Media",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "MediaId",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "MediaKind",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "MediaPath",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "MediaState",
                table: "WhatsAppMessages");
        }
    }
}
