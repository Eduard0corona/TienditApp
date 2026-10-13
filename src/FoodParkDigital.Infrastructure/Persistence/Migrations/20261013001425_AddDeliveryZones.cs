using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryZones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeliveryZones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Fee = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MinimumOrder = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    FreeDeliveryFrom = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryZones", x => x.Id);
                    table.CheckConstraint("CK_DeliveryZones_Fee", "[Fee] >= 0 AND [Fee] <= 10000");
                    table.CheckConstraint("CK_DeliveryZones_FreeDeliveryFrom", "[FreeDeliveryFrom] IS NULL OR ([FreeDeliveryFrom] > 0 AND [FreeDeliveryFrom] <= 100000 AND [Fee] > 0)");
                    table.CheckConstraint("CK_DeliveryZones_MinimumOrder", "[MinimumOrder] >= 0 AND [MinimumOrder] <= 100000");
                });

            migrationBuilder.CreateTable(
                name: "DeliveryZonePostalCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeliveryZoneId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PostalCode = table.Column<string>(type: "varchar(5)", unicode: false, maxLength: 5, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryZonePostalCodes", x => x.Id);
                    table.CheckConstraint("CK_DeliveryZonePostalCodes_PostalCode", "LEN([PostalCode]) = 5 AND [PostalCode] NOT LIKE '%[^0-9]%'");
                    table.ForeignKey(
                        name: "FK_DeliveryZonePostalCodes_DeliveryZones_DeliveryZoneId",
                        column: x => x.DeliveryZoneId,
                        principalTable: "DeliveryZones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryZonePostalCodes_DeliveryZoneId",
                table: "DeliveryZonePostalCodes",
                column: "DeliveryZoneId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryZonePostalCodes_TenantId_PostalCode",
                table: "DeliveryZonePostalCodes",
                columns: new[] { "TenantId", "PostalCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryZones_TenantId_Name",
                table: "DeliveryZones",
                columns: new[] { "TenantId", "Name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The delivery zones of the businesses would be lost, and the activity entries keep their names, fees and how
            // many postal codes they had, never which ones. With zones saved there is only roll-forward, or deleting them
            // on purpose first. A table that is already gone compiles all the same (deferred name resolution), so a second
            // run of rollback.sql does nothing.
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM [DeliveryZones])
    THROW 50012, N'There are delivery zones saved; rolling back would delete them. Delete them first, or roll forward.', 1;
");

            migrationBuilder.DropTable(
                name: "DeliveryZonePostalCodes");

            migrationBuilder.DropTable(
                name: "DeliveryZones");
        }
    }
}
