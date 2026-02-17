using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnerWhatsAppConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OwnerWhatsAppConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    UseForOtp = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    AccessToken = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PhoneNumberId = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    WabaId = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    DisplayPhoneNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    OtpTemplateName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, defaultValue: "otp_verification"),
                    OtpTemplateLanguageCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false, defaultValue: "es_MX"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnerWhatsAppConfigs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OwnerWhatsAppConfigs_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OwnerWhatsAppConfigs_UserId",
                table: "OwnerWhatsAppConfigs",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OwnerWhatsAppConfigs");
        }
    }
}
