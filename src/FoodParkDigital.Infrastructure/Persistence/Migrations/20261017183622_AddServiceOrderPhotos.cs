using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceOrderPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ServiceOrderPhotos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Stage = table.Column<int>(type: "int", nullable: false),
                    FileUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    TakenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TakenBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    TakenByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceOrderPhotos", x => x.Id);
                    table.CheckConstraint("CK_ServiceOrderPhotos_Stage", "[Stage] IN (0, 1)");
                    table.ForeignKey(
                        name: "FK_ServiceOrderPhotos_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOrderPhotos_OrderId",
                table: "ServiceOrderPhotos",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOrderPhotos_TenantId_OrderId_TakenAt",
                table: "ServiceOrderPhotos",
                columns: new[] { "TenantId", "OrderId", "TakenAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rolling back deletes the photos the businesses took of their services: the record of what they received and
            // delivered. Once there is one there is only roll-forward. A missing table compiles (deferred name resolution):
            // a second run of rollback.sql skips it.
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM [ServiceOrderPhotos])
    THROW 50018, N'Businesses added photos to their service orders; rolling back would delete them. Roll forward instead.', 1;
");

            migrationBuilder.DropTable(
                name: "ServiceOrderPhotos");
        }
    }
}
