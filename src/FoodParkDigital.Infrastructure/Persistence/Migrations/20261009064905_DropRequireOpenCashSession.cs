using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Drops TenantSettings.RequireOpenCashSession with its default: the model stopped mapping it in
    /// StopMappingRequireOpenCashSession, so the version running while this is applied (the one with that migration) no
    /// longer reads it. Written by hand, because the model has no such column. Down creates it again with its default,
    /// 0, for the version before that migration, which maps it; the values do not come back. That version ignores them:
    /// every payment and refund needs an open drawer.
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
