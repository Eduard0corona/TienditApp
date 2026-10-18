using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFavoriteBusinesses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FavoriteBusinesses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FavoriteBusinesses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FavoriteBusinesses_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FavoriteBusinesses_Tenants_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FavoriteBusinesses_BusinessId",
                table: "FavoriteBusinesses",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_FavoriteBusinesses_UserId_BusinessId",
                table: "FavoriteBusinesses",
                columns: new[] { "UserId", "BusinessId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rolling back deletes the businesses customers kept among their favorites: they are theirs, not a
            // business's, so once there is one there is only roll-forward. A missing table compiles (deferred name
            // resolution): a second run of rollback.sql skips it.
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM [FavoriteBusinesses])
    THROW 50021, N'Customer accounts saved favorite businesses; rolling back would delete them. Roll forward instead.', 1;
");

            migrationBuilder.DropTable(
                name: "FavoriteBusinesses");
        }
    }
}
