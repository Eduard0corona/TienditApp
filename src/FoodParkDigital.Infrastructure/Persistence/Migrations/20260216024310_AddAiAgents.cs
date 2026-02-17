using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiAgents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiAgents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Personality = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Language = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Tone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    BusinessName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BusinessDescription = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    BusinessProductsAndServices = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    WelcomeMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    FallbackMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ClosingMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    AskForName = table.Column<bool>(type: "bit", nullable: false),
                    AskForPhone = table.Column<bool>(type: "bit", nullable: false),
                    AskForEmail = table.Column<bool>(type: "bit", nullable: false),
                    AskForAddress = table.Column<bool>(type: "bit", nullable: false),
                    CustomQuestions = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Temperature = table.Column<decimal>(type: "decimal(4,2)", nullable: false),
                    MaxTokens = table.Column<int>(type: "int", nullable: false),
                    UseConversationMemory = table.Column<bool>(type: "bit", nullable: false),
                    StrictMode = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiAgents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiAgents_TenantId_IsActive",
                table: "AiAgents",
                columns: new[] { "TenantId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_AiAgents_TenantId_Name",
                table: "AiAgents",
                columns: new[] { "TenantId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiAgents");
        }
    }
}
