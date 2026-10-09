using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The model stops mapping TenantSettings.RequireOpenCashSession, the optional policy that refused cash payments
    /// without an open drawer: every payment and refund now needs one, so nothing reads it. It changes nothing in the
    /// database. The version running while this is applied still reads the column, so the next migration drops it,
    /// once no version maps it. This version saves settings without it: new rows take its default, 0.
    /// </summary>
    public partial class StopMappingRequireOpenCashSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
