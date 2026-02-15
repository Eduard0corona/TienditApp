using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WhatsAppConversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerPhone = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LastMessageBody = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LastMessageAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UnreadCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppConversations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ExternalMessageId = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: true),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WhatsAppMessages_WhatsAppConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "WhatsAppConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppConversations_TenantId_CustomerPhone",
                table: "WhatsAppConversations",
                columns: new[] { "TenantId", "CustomerPhone" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppConversations_TenantId_LastMessageAt",
                table: "WhatsAppConversations",
                columns: new[] { "TenantId", "LastMessageAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_ConversationId_SentAt",
                table: "WhatsAppMessages",
                columns: new[] { "ConversationId", "SentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_ExternalMessageId",
                table: "WhatsAppMessages",
                column: "ExternalMessageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WhatsAppMessages");

            migrationBuilder.DropTable(
                name: "WhatsAppConversations");
        }
    }
}
