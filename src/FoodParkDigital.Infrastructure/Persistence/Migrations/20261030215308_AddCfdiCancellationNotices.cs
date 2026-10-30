using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCfdiCancellationNotices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "CfdiEmails",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "CK_CfdiEmails_Kind",
                table: "CfdiEmails",
                sql: "[Kind] IN (0, 1) AND ([Kind] = 0 OR ([SentWithoutPdf] = 0 AND [RequestedByUserId] IS NULL))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Going back would turn the notices of cancelled CFDI into emails of their files: stop.
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [CfdiEmails] WHERE [Kind] <> 0)
    THROW 50047, N''Notices of cancelled CFDI are recorded already; rolling back would lose them. Roll forward instead.'', 1;');
");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CfdiEmails_Kind",
                table: "CfdiEmails");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "CfdiEmails");
        }
    }
}
