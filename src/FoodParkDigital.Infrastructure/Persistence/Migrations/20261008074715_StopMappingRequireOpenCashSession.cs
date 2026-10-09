using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The model stops mapping TenantSettings.RequireOpenCashSession: a policy no longer read, since every payment and
    /// refund needs an open drawer whatever the business chose. It changes nothing in the database. The version
    /// running while this is applied still maps the column, so it is dropped by a later migration, once no version
    /// maps it. This version saves settings without it: the column takes its default, 0.
    /// <para>
    /// Fase 15 did this step on its branch while PR #47 did the same on master, as
    /// 20261009041644_StopMappingRequireOpenCashSession. Both only update the snapshot and both stay: a database may
    /// have recorded either one, and neither changes anything. The class is named after the phase so the two compile
    /// together; its migration id is the one the Fase 15 scripts recorded.
    /// </para>
    /// </summary>
    public partial class StopMappingRequireOpenCashSessionPhase15 : Migration
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
