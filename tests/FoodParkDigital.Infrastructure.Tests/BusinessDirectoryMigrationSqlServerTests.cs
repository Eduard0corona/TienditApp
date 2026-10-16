using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// AddBusinessDirectory (Fase 24A, ADR 0021): each existing business starts in the category BusinessCategoryDefaults gives
/// its giro and type, listed and without description; the version deployed before keeps opening and editing businesses
/// over the new columns; the checks keep every row one the domain could write; and rolling back stops with error 50016
/// once an owner decided how the business shows in Explorar, and otherwise drops only what it added.
/// </summary>
public class BusinessDirectoryMigrationSqlServerTests
{
    // The migration right before AddBusinessDirectory, where its rollback.sql takes the database.
    private const string BeforeDirectory = "20261013191311_AddHomeVisits";
    private const string Migration = "20261016162347_AddBusinessDirectory";

    /// <summary>
    /// The giros of the sign-up list as it stores them, the same written with other capitals, accents or spaces, and the
    /// ones it does not know: «Otro», one written by hand, none.
    /// </summary>
    private static readonly string?[] Industries =
    [
        "Alimentos y bebidas", "Comida a domicilio", "Abarrotes", "Farmacia", "Ropa y accesorios", "Electrónica", "Ferretería",
        "Papelería", "Estética y spa", "Taller mecánico", "Consultorio", "Servicios profesionales", "Educación", "Gimnasio",
        "Lavandería", "  FERRETERÍA ", "lavanderia", "Taller Mecanico", "Alimentos y Bebidas", "Otro", "Venta de flores", "",
        null,
    ];

    [SqlServerFact]
    public async Task Backfill_ShouldGiveEachBusinessTheCategoryOfItsGiroOrType_AsANewOneGetsIt()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeDirectory);
        var expected = new Dictionary<Guid, BusinessCategory>();
        await using (var seed = BeforeBusinessDirectory.CreateContext(database))
        {
            foreach (var industry in Industries)
            foreach (var type in Enum.GetValues<BusinessType>())
            {
                var tenant = NewTenant(type);
                tenant.SetBusinessInfo(null, null, industry, null, null);
                seed.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Food));
                expected.Add(tenant.Id, BusinessCategoryDefaults.Suggest(industry, type));
            }
            await seed.SaveChangesAsync();
        }

        await MigrateAsync(database, Migration);

        await using var check = database.CreateContext();
        var tenants = await check.Tenants.ToListAsync();
        Assert.Equal(expected.Count, tenants.Count);
        foreach (var tenant in tenants)
        {
            Assert.True(expected[tenant.Id] == tenant.DirectoryCategory,
                $"«{tenant.Industry}» ({tenant.BusinessType}): {tenant.DirectoryCategory}, expected {expected[tenant.Id]}");
            Assert.Null(tenant.PublicDescription);
        }
        Assert.Equal(expected.Count, await CountAsync(check, $"SELECT COUNT(*) AS [Value] FROM [TenantSettings] WHERE [ListedInDirectory] = 1"));
        // Every category of the giros is reached, and a shop without a giro it knows waits for its owner in Otros.
        Assert.Equal(11, tenants.Select(t => t.DirectoryCategory).Distinct().Count());
    }

    [SqlServerFact]
    public async Task PreviousVersion_ShouldKeepOpeningAndEditingBusinesses_WhichStartListedInOtros()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var tenant = await SeedAsync(database, BusinessType.Food);
        await using (var context = database.CreateContext(tenant.Id))
        {
            (await context.Tenants.SingleAsync()).SetDirectoryProfile(BusinessCategory.Groceries, "Abarrotes y tortillas");
            (await context.TenantSettings.SingleAsync()).SetDirectoryListing(false);
            await context.SaveChangesAsync();
        }

        // The version deployed before does not know the columns: it edits a business and its settings, which keep what its
        // owner decided, and opens another, which takes the defaults.
        await using (var previous = BeforeBusinessDirectory.CreateContext(database, tenant.Id))
        {
            var edited = await previous.Tenants.SingleAsync();
            edited.Update("Tienda de Lupita", edited.ContactEmail, "5512345678", null);
            (await previous.TenantSettings.SingleAsync()).Update("MXN", "America/Mexico_City", autoAcceptOrders: true, 16);
            await previous.SaveChangesAsync();
        }
        var opened = NewTenant(BusinessType.Food);
        await using (var previous = BeforeBusinessDirectory.CreateContext(database, opened.Id))
        {
            previous.AddRange(opened, new TenantSettings(opened.Id, TenantCapability.Food));
            await previous.SaveChangesAsync();
        }

        await using (var check = database.CreateContext(tenant.Id))
        {
            var kept = await check.Tenants.SingleAsync(t => t.Id == tenant.Id);
            Assert.Equal(("Tienda de Lupita", BusinessCategory.Groceries, "Abarrotes y tortillas", false),
                (kept.Name, kept.DirectoryCategory, kept.PublicDescription, (await check.TenantSettings.SingleAsync()).ListedInDirectory));
        }
        await using var fresh = database.CreateContext(opened.Id);
        var started = await fresh.Tenants.SingleAsync(t => t.Id == opened.Id);
        Assert.Equal((BusinessCategory.Other, (string?)null, true),
            (started.DirectoryCategory, started.PublicDescription, (await fresh.TenantSettings.SingleAsync()).ListedInDirectory));
    }

    [SqlServerFact]
    public async Task Checks_ShouldKeepEveryRowOneTheDomainWrites()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var tenant = await SeedAsync(database, BusinessType.Retail);

        foreach (var (sql, constraint) in new (FormattableString, string)[]
        {
            ($"UPDATE [Tenants] SET [DirectoryCategory] = 11 WHERE [Id] = {tenant.Id}", "CK_Tenants_DirectoryCategory"),
            ($"UPDATE [Tenants] SET [DirectoryCategory] = -1 WHERE [Id] = {tenant.Id}", "CK_Tenants_DirectoryCategory"),
            ($"UPDATE [Tenants] SET [PublicDescription] = N'' WHERE [Id] = {tenant.Id}", "CK_Tenants_PublicDescription"),
        })
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(database, sql));
            Assert.Equal(547, error.Number);
            Assert.Contains(constraint, error.Message);
        }

        // 160 characters fit, one more does not.
        await ExecuteAsync(database, $"UPDATE [Tenants] SET [PublicDescription] = REPLICATE(N'a', 160) WHERE [Id] = {tenant.Id}");
        await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(database,
            $"UPDATE [Tenants] SET [PublicDescription] = REPLICATE(CAST(N'a' AS nvarchar(max)), 161) WHERE [Id] = {tenant.Id}"));
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_OnceAnOwnerDecided_AndThenDropOnlyWhatItAdded()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var tenant = await SeedAsync(database, BusinessType.Service);
        await using (var context = database.CreateContext(tenant.Id))
        {
            (await context.Tenants.SingleAsync()).SetDirectoryProfile(BusinessCategory.Beauty, "Cortes y peinados");
            (await context.TenantSettings.SingleAsync()).SetDirectoryListing(false);
            // A change of category alone leaves no other trace.
            context.AuditLogs.Add(new AuditLog(tenant.Id, "Tenant", tenant.Id.ToString(), "DirectoryProfileUpdated", "duena@demo.com"));
            await context.SaveChangesAsync();
        }

        // A hidden business stops it; so does a description alone, and then an audited change alone, each undone on purpose.
        foreach (var cleanUp in new[]
        {
            "UPDATE [TenantSettings] SET [ListedInDirectory] = 1", "UPDATE [Tenants] SET [PublicDescription] = NULL",
            "DELETE FROM [AuditLogs] WHERE [Action] = N'DirectoryProfileUpdated'",
        })
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(database, BeforeDirectory));
            Assert.Equal(50016, error.Number);
            Assert.Contains("Owners already decided how their businesses show in Explorar", error.Message);
            await using var context = database.CreateContext();
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            await context.Database.ExecuteSqlRawAsync(cleanUp);
        }

        await MigrateAsync(database, BeforeDirectory);
        await using (var context = database.CreateContext())
        {
            Assert.Equal(BeforeDirectory, await LatestMigrationAsync(context));
            Assert.Equal(0, await CountAsync(context, $"""
                SELECT COUNT(*) AS [Value] FROM sys.columns
                WHERE ([object_id] = OBJECT_ID(N'dbo.Tenants') AND [name] IN (N'DirectoryCategory', N'PublicDescription'))
                    OR ([object_id] = OBJECT_ID(N'dbo.TenantSettings') AND [name] = N'ListedInDirectory')
                """));
            Assert.Equal(0, await CountAsync(context, $"""
                SELECT COUNT(*) AS [Value] FROM sys.check_constraints
                WHERE [name] IN (N'CK_Tenants_DirectoryCategory', N'CK_Tenants_PublicDescription')
                """));
            Assert.Equal((1, 1), (
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [Tenants]"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [TenantSettings]")));
        }

        // Rolling back again does nothing, and forward again the business is listed in the category of its type.
        await MigrateAsync(database, BeforeDirectory);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext(tenant.Id);
        var again = await forward.Tenants.SingleAsync();
        Assert.Equal((BusinessCategory.Services, (string?)null, true),
            (again.DirectoryCategory, again.PublicDescription, (await forward.TenantSettings.SingleAsync()).ListedInDirectory));
    }

    private static async Task<Tenant> SeedAsync(SqlServerTestDatabase database, BusinessType type)
    {
        var tenant = NewTenant(type);
        await using var seed = database.CreateContext();
        seed.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Food));
        await seed.SaveChangesAsync();
        return tenant;
    }

    private static Tenant NewTenant(BusinessType type)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        return new Tenant($"Negocio {slug}", slug, type, $"{slug}@demo.com");
    }

    /// <summary>Applies the migrations up to <paramref name="target"/>, as a deployment does.</summary>
    private static async Task MigrateAsync(SqlServerTestDatabase database, string target)
    {
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(target);
    }

    private static async Task ExecuteAsync(SqlServerTestDatabase database, FormattableString sql)
    {
        await using var context = database.CreateContext();
        await context.Database.ExecuteSqlInterpolatedAsync(sql);
    }

    private static Task<string> LatestMigrationAsync(ApplicationDbContext context) =>
        context.Database.SqlQuery<string>($"""
            SELECT TOP (1) [MigrationId] AS [Value] FROM [__EFMigrationsHistory] ORDER BY [MigrationId] DESC
            """).SingleAsync();

    private static Task<int> CountAsync(ApplicationDbContext context, FormattableString sql) =>
        context.Database.SqlQuery<int>(sql).SingleAsync();
}
