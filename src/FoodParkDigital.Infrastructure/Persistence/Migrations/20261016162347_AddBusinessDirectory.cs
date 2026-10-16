using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessDirectory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ListedInDirectory",
                table: "TenantSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "DirectoryCategory",
                table: "Tenants",
                type: "int",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<string>(
                name: "PublicDescription",
                table: "Tenants",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true);

            // Each existing business starts in the category of its giro, as BusinessCategoryDefaults.Suggest gives a new
            // one: the giros of the sign-up list compared without accents or capitals (Latin1_General_CI_AI), else its
            // type (food to 0, services to 9, a shop to 10). The giros are written without accents, which that comparison
            // ignores, so the script reads the same in any code page. Through EXEC: the column was added in this same
            // batch.
            migrationBuilder.Sql(@"
EXEC(N'UPDATE [Tenants] SET [DirectoryCategory] = CASE
    WHEN LTRIM(RTRIM([Industry])) COLLATE Latin1_General_CI_AI IN (N''Alimentos y bebidas'', N''Comida a domicilio'') THEN 0
    WHEN LTRIM(RTRIM([Industry])) COLLATE Latin1_General_CI_AI = N''Abarrotes'' THEN 1
    WHEN LTRIM(RTRIM([Industry])) COLLATE Latin1_General_CI_AI IN (N''Farmacia'', N''Consultorio'') THEN 2
    WHEN LTRIM(RTRIM([Industry])) COLLATE Latin1_General_CI_AI = N''Ropa y accesorios'' THEN 3
    WHEN LTRIM(RTRIM([Industry])) COLLATE Latin1_General_CI_AI = N''Electronica'' THEN 4
    WHEN LTRIM(RTRIM([Industry])) COLLATE Latin1_General_CI_AI = N''Ferreteria'' THEN 5
    WHEN LTRIM(RTRIM([Industry])) COLLATE Latin1_General_CI_AI = N''Papeleria'' THEN 6
    WHEN LTRIM(RTRIM([Industry])) COLLATE Latin1_General_CI_AI IN (N''Estetica y spa'', N''Gimnasio'') THEN 7
    WHEN LTRIM(RTRIM([Industry])) COLLATE Latin1_General_CI_AI = N''Taller mecanico'' THEN 8
    WHEN LTRIM(RTRIM([Industry])) COLLATE Latin1_General_CI_AI IN (N''Servicios profesionales'', N''Educacion'', N''Lavanderia'') THEN 9
    WHEN [BusinessType] = 0 THEN 0
    WHEN [BusinessType] = 2 THEN 9
    ELSE 10
END;');
");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tenants_DirectoryCategory",
                table: "Tenants",
                sql: "[DirectoryCategory] >= 0 AND [DirectoryCategory] <= 10");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tenants_PublicDescription",
                table: "Tenants",
                sql: "[PublicDescription] IS NULL OR LEN([PublicDescription]) > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rolling back loses what owners decided about Explorar: who hid the business, its description and the category
            // it picked. Applying again would list a business its owner hid, so once an owner decided there is only
            // roll-forward. Every change through the app is audited as DirectoryProfileUpdated. Through EXEC: on a second
            // run of rollback.sql the columns are gone, and a batch that names them would not compile.
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [TenantSettings] WHERE [ListedInDirectory] = 0)
    OR EXISTS (SELECT 1 FROM [Tenants] WHERE [PublicDescription] IS NOT NULL)
    OR EXISTS (SELECT 1 FROM [AuditLogs] WHERE [Action] = N''DirectoryProfileUpdated'')
    THROW 50016, N''Owners already decided how their businesses show in Explorar; rolling back would lose it and list again those they hid. Roll forward instead.'', 1;');
");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Tenants_DirectoryCategory",
                table: "Tenants");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Tenants_PublicDescription",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ListedInDirectory",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "DirectoryCategory",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "PublicDescription",
                table: "Tenants");
        }
    }
}
