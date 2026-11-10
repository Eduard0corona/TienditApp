using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueCategoryNamePerTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Two categories of a business with the same name (by the collation of the database, as the index compares
            // them) stop it before anything changes: query 3 of its verify.sql lists them, to rename one first (in the app,
            // or with data-repair/20261110_RepeatedCategoryNames). The lock holds until the index exists, in the same
            // transaction: no category the deployed version saves in between can repeat a name.
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [Categories] WITH (TABLOCKX, HOLDLOCK) GROUP BY [TenantId], [Name] HAVING COUNT(*) > 1)
    THROW 50057, N''Two categories of a business have the same name; rename one (query 3 of verify.sql) before applying this migration.'', 1;');
");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_TenantId_Name",
                table: "Categories",
                columns: new[] { "TenantId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Categories_TenantId_Name",
                table: "Categories");
        }
    }
}
