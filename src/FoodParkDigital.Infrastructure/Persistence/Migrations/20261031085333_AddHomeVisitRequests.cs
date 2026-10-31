using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHomeVisitRequests : Migration
    {
        // The giros of HomeTrades, written without accents, which Latin1_General_CI_AI ignores: the script reads the same
        // in any code page.
        private const string Trades = @"N'Carpinteria', N'Herreria y soldadura', N'Electricista', N'Plomeria', N'Aire acondicionado',
        N'Fumigacion', N'Refrigeracion y linea blanca', N'Cerrajeria', N'Jardineria', N'Limpieza del hogar',
        N'Pintura e impermeabilizacion'";

        // The category BusinessCategoryDefaults gave before «Hogar y reparaciones»: the five trades of the list then to
        // Servicios (9), the rest by type (food to Comida, services to Servicios and a shop to Otros).
        private const string PreviousCategory = @"CASE
    WHEN LTRIM(RTRIM(t.[Industry])) COLLATE Latin1_General_CI_AI IN (
        N'Carpinteria', N'Herreria y soldadura', N'Electricista', N'Plomeria', N'Aire acondicionado') THEN 9
    WHEN t.[BusinessType] = 0 THEN 0
    WHEN t.[BusinessType] = 2 THEN 9
    ELSE 10
END";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Tenants_DirectoryCategory",
                table: "Tenants");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HomeVisitAddressNoticeDismissedAt",
                table: "TenantSettings",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HomeVisitChargePolicy",
                table: "TenantSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "HomeVisitRequestsEnabled",
                table: "TenantSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "HomeVisitServiceId",
                table: "TenantSettings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_TenantSettings_HomeVisitChargePolicy",
                table: "TenantSettings",
                sql: "[HomeVisitChargePolicy] >= 0 AND [HomeVisitChargePolicy] <= 2");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tenants_DirectoryCategory",
                table: "Tenants",
                sql: "[DirectoryCategory] >= 0 AND [DirectoryCategory] <= 11");

            // A trade whose category still follows its giro moves to «Hogar y reparaciones», as BusinessCategoryDefaults gives
            // a new one (ADR 0053). It follows its giro when it is the one the giro gave until now (Tenant.SetBusinessInfo:
            // the five trades of the list then to Servicios, the rest by type) and its owner never decided it (no
            // DirectoryProfileUpdated in the audit, ADR 0021): a locksmith signed up as a shop leaves Otros too. Nothing
            // else changes for the businesses already signed up: their visits to quote stay off.
            migrationBuilder.Sql($@"
UPDATE t SET [DirectoryCategory] = 11
FROM [Tenants] AS t
WHERE LTRIM(RTRIM(t.[Industry])) COLLATE Latin1_General_CI_AI IN (
        {Trades})
    AND t.[DirectoryCategory] = {PreviousCategory}
    AND NOT EXISTS (
        SELECT 1 FROM [AuditLogs] AS a
        WHERE a.[EntityName] = N'Tenant' AND a.[Action] = N'DirectoryProfileUpdated'
            AND a.[EntityId] = LOWER(CONVERT(nvarchar(36), t.[Id])));
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rolling back loses what owners decided: a business that picked «Hogar y reparaciones» (audited as
            // DirectoryProfileUpdated) and how a business receives visits to quote (HomeVisitRequestsUpdated). Once one
            // did, there is only roll-forward. A trade that only followed its giro goes back to the category its giro gave
            // before (the five trades of the list then to Servicios, the rest by type, as AddBusinessDirectory). Through
            // EXEC: on a second run of rollback.sql the columns are gone, and a batch that names them would not compile.
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (
        SELECT 1 FROM [Tenants] AS t
        WHERE t.[DirectoryCategory] = 11 AND EXISTS (
            SELECT 1 FROM [AuditLogs] AS a
            WHERE a.[EntityName] = N''Tenant'' AND a.[Action] = N''DirectoryProfileUpdated''
                AND a.[EntityId] = LOWER(CONVERT(nvarchar(36), t.[Id]))))
    OR EXISTS (SELECT 1 FROM [AuditLogs] WHERE [Action] = N''HomeVisitRequestsUpdated'')
    THROW 50049, N''Owners already decided their visits to quote or picked Hogar y reparaciones in Explorar; rolling back would lose it. Roll forward instead.'', 1;');
");

            // Through EXEC too: rolling back every migration after this one's base drops [DirectoryCategory]
            // (AddBusinessDirectory), and a second run would not compile a batch that names it.
            migrationBuilder.Sql($@"
EXEC(N'UPDATE t SET [DirectoryCategory] = {PreviousCategory.Replace("'", "''")}
FROM [Tenants] AS t
WHERE t.[DirectoryCategory] = 11;');
");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TenantSettings_HomeVisitChargePolicy",
                table: "TenantSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Tenants_DirectoryCategory",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "HomeVisitAddressNoticeDismissedAt",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "HomeVisitChargePolicy",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "HomeVisitRequestsEnabled",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "HomeVisitServiceId",
                table: "TenantSettings");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tenants_DirectoryCategory",
                table: "Tenants",
                sql: "[DirectoryCategory] >= 0 AND [DirectoryCategory] <= 10");
        }
    }
}
