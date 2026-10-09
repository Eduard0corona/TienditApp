using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Drops TenantSettings.RequireOpenCashSession with its default: the model stopped mapping it in
    /// StopMappingRequireOpenCashSession (PR #47) and StopMappingRequireOpenCashSessionPhase15, so the version running
    /// while this is applied (one with those migrations) no longer reads it. Written by hand, because the model has no
    /// such column. Down creates it again with its default, 0, for a version before them, which maps it; the values do
    /// not come back. Since PR #40 no version uses them: every payment and refund needs an open drawer.
    /// </summary>
    public partial class DropRequireOpenCashSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequireOpenCashSession",
                table: "TenantSettings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequireOpenCashSession",
                table: "TenantSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }
    }
}
