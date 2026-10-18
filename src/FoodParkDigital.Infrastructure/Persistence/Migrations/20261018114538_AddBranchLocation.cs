using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBranchLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Latitude",
                table: "Branches",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Longitude",
                table: "Branches",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Branches_Location",
                table: "Branches",
                sql: "([Latitude] IS NULL AND [Longitude] IS NULL) OR ([Latitude] IS NOT NULL AND [Longitude] IS NOT NULL AND [Latitude] BETWEEN -90 AND 90 AND [Longitude] BETWEEN -180 AND 180)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rolling back forgets where owners put their branches on the map. Once there is one there is only
            // roll-forward. A missing column compiles inside EXEC: a second run of rollback.sql skips it.
            migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.Branches', N'Latitude') IS NOT NULL
    EXEC(N'IF EXISTS (SELECT 1 FROM [Branches] WHERE [Latitude] IS NOT NULL)
    THROW 50022, N''Owners put branches on the map; rolling back would lose them. Roll forward instead.'', 1;');
");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Branches_Location",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "Branches");
        }
    }
}
