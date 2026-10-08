using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The model stops mapping TenantSettings.RequireOpenCashSession: a policy no longer read, since every payment and
    /// refund needs an open drawer whatever the business chose. It changes nothing in the database. The version
    /// running while this is applied still maps the column, so it is dropped by a later migration, once no version
    /// maps it. This version saves settings without it: the column takes its default, 0.
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
