using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderTrackingToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TrackingToken",
                table: "Orders",
                type: "varchar(43)",
                unicode: false,
                maxLength: 43,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TrackingToken",
                table: "Orders",
                column: "TrackingToken",
                unique: true,
                filter: "[TrackingToken] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_TrackingToken",
                table: "Orders",
                sql: "[TrackingToken] IS NULL OR LEN([TrackingToken]) = 43");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_TrackingToken",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_TrackingToken",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TrackingToken",
                table: "Orders");
        }
    }
}
