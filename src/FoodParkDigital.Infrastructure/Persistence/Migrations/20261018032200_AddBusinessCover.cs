using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessCover : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CoverImageUrl",
                table: "Tenants",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rolling back forgets the covers owners uploaded, and their files would stay in the storage with nothing
            // pointing at them. Once there is one there is only roll-forward. A missing column compiles inside EXEC: a
            // second run of rollback.sql skips it.
            migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.Tenants', N'CoverImageUrl') IS NOT NULL
    EXEC(N'IF EXISTS (SELECT 1 FROM [Tenants] WHERE [CoverImageUrl] IS NOT NULL)
    THROW 50020, N''Owners uploaded menu covers; rolling back would lose them. Roll forward instead.'', 1;');
");

            migrationBuilder.DropColumn(
                name: "CoverImageUrl",
                table: "Tenants");
        }
    }
}
