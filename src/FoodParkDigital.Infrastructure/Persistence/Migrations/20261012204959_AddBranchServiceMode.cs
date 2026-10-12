using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBranchServiceMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ServiceMode",
                table: "Branches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Branches_ServiceMode",
                table: "Branches",
                sql: "[ServiceMode] IN (0, 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Without the column every branch has premises again, and the previous version publishes the address of the
            // main one on the online menu: for a branch that only delivers, often a home. Change those branches to
            // premises (and clear their street) on purpose first, or roll forward.
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM [Branches] WHERE [ServiceMode] <> 0)
    THROW 50011, N'There are branches that only deliver; rolling back would publish the address of the main one. Change them to premises (and clear their street) first, or roll forward.', 1;
");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Branches_ServiceMode",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "ServiceMode",
                table: "Branches");
        }
    }
}
