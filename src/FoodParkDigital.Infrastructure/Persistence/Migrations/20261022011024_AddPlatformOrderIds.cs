using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformOrderIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalOrderId",
                table: "PlatformOrderDetails",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformOrderDetails_TenantId_Channel_ExternalOrderId",
                table: "PlatformOrderDetails",
                columns: new[] { "TenantId", "Channel", "ExternalOrderId" },
                filter: "[ExternalOrderId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PlatformOrderDetails_ExternalOrderId",
                table: "PlatformOrderDetails",
                sql: "[ExternalOrderId] IS NULL OR LEN([ExternalOrderId]) >= 6");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The long ids are what a business wrote to find its orders in a platform's statement: rolling back would erase
            // them, and nothing could write them again. Once one is written there is only roll-forward. The check runs as
            // dynamic SQL, as the previous rollbacks do.
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [PlatformOrderDetails] WHERE [ExternalOrderId] IS NOT NULL)
    THROW 50032, N''Businesses wrote the long id of orders of the delivery platforms already; rolling back would erase them. Roll forward instead.'', 1;');
");

            migrationBuilder.DropIndex(
                name: "IX_PlatformOrderDetails_TenantId_Channel_ExternalOrderId",
                table: "PlatformOrderDetails");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PlatformOrderDetails_ExternalOrderId",
                table: "PlatformOrderDetails");

            migrationBuilder.DropColumn(
                name: "ExternalOrderId",
                table: "PlatformOrderDetails");
        }
    }
}
