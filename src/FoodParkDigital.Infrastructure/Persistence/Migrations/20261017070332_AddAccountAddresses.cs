using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountAddresses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountAddresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    PostalCode = table.Column<string>(type: "varchar(5)", unicode: false, maxLength: 5, nullable: false),
                    AddressLine = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    References = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountAddresses", x => x.Id);
                    table.CheckConstraint("CK_AccountAddresses_PostalCode", "LEN([PostalCode]) = 5 AND [PostalCode] NOT LIKE '%[^0-9]%'");
                    table.ForeignKey(
                        name: "FK_AccountAddresses_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryZonePostalCodes_PostalCode",
                table: "DeliveryZonePostalCodes",
                column: "PostalCode")
                .Annotation("SqlServer:Include", new[] { "TenantId", "DeliveryZoneId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountAddresses_UserId_LastUsedAt",
                table: "AccountAddresses",
                columns: new[] { "UserId", "LastUsedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountAddresses");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryZonePostalCodes_PostalCode",
                table: "DeliveryZonePostalCodes");
        }
    }
}
