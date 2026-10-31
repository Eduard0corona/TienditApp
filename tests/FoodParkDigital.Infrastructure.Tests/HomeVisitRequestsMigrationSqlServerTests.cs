using FoodParkDigital.Domain.Appointments.HomeVisits;
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
/// AddHomeVisitRequests (Fase 29A, ADR 0053): the settings of a business keep whether it receives visits to quote, its visit
/// service, how it charges the visit and whether it dismissed the notice of its published address; Explorar gets «Hogar y
/// reparaciones» (11). The settings that were there, and those the previous version keeps saving, receive no visits; the
/// trades whose category followed their giro move to 11 and no other business changes; the database refuses what the
/// domain never writes, and rolling back stops with error 50049 once an owner decided something it would lose.
/// </summary>
public class HomeVisitRequestsMigrationSqlServerTests
{
    // The migration right before AddHomeVisitRequests, where its rollback.sql takes the database.
    private const string BeforeMigration = "20261031053720_AddAccountantCfdiEmails";
    private const string Migration = "20261031085333_AddHomeVisitRequests";

    [SqlServerFact]
    public async Task Trades_ShouldMoveToHomeRepairs_OnlyWhereTheirCategoryFollowedTheGiro_AndNobodyStartsReceivingVisits()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeMigration);
        // As the version before this one leaves them, in the category their giro gave: the five trades of the list then
        // in Servicios (9), the new ones by type.
        var plumber = await SeedAsync(database, "Plomería", BusinessCategory.Services);
        var withoutAccents = await SeedAsync(database, "  plomeria ", BusinessCategory.Services);
        var pestControl = await SeedAsync(database, "Fumigación", BusinessCategory.Services, BusinessType.Service);
        var locksmith = await SeedAsync(database, "Cerrajería", BusinessCategory.Other);
        // Not the one its giro gave (a shop goes to Otros): it does not follow it.
        var gardener = await SeedAsync(database, "Jardinería", BusinessCategory.Services);
        var decided = await SeedAsync(database, "Electricista", BusinessCategory.Services, ownerDecided: true);
        var elsewhere = await SeedAsync(database, "Carpintería", BusinessCategory.Hardware, ownerDecided: true);
        var professional = await SeedAsync(database, "Servicios profesionales", BusinessCategory.Services);
        var shop = await SeedAsync(database, "Abarrotes", BusinessCategory.Groceries);

        await MigrateAsync(database, Migration);

        await using (var context = database.CreateContext())
        {
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            var categories = await context.Tenants.IgnoreQueryFilters().ToDictionaryAsync(t => t.Id, t => t.DirectoryCategory);
            Assert.Equal(BusinessCategory.HomeRepairs, categories[plumber]);
            Assert.Equal(BusinessCategory.HomeRepairs, categories[withoutAccents]);
            Assert.Equal(BusinessCategory.HomeRepairs, categories[pestControl]);
            Assert.Equal(BusinessCategory.HomeRepairs, categories[locksmith]);
            Assert.Equal(BusinessCategory.Services, categories[gardener]);
            // Its owner decided it: it stays where it is, also in Servicios.
            Assert.Equal(BusinessCategory.Services, categories[decided]);
            Assert.Equal(BusinessCategory.Hardware, categories[elsewhere]);
            Assert.Equal(BusinessCategory.Services, categories[professional]);
            Assert.Equal(BusinessCategory.Groceries, categories[shop]);
            // Nobody starts receiving visits: the businesses already signed up only get a notice (ADR 0053).
            var settings = await context.TenantSettings.IgnoreQueryFilters().ToListAsync();
            Assert.Equal(9, settings.Count);
            Assert.All(settings, s => Assert.Equal(
                (false, (Guid?)null, HomeVisitChargePolicy.Charged, (DateTimeOffset?)null),
                (s.HomeVisitRequestsEnabled, s.HomeVisitServiceId, s.HomeVisitChargePolicy, s.HomeVisitAddressNoticeDismissedAt)));
        }

        // The deployed version keeps saving the settings while this one is published.
        await using (var previous = BeforeHomeVisitRequests.CreateContext(database, plumber))
        {
            (await previous.TenantSettings.SingleAsync()).SetHomeVisitTravelMinutes(45);
            await previous.SaveChangesAsync();
        }
        var service = Guid.NewGuid();
        await using (var context = database.CreateContext(plumber))
        {
            var settings = await context.TenantSettings.SingleAsync();
            Assert.Equal((45, false), (settings.HomeVisitTravelMinutes, settings.HomeVisitRequestsEnabled));
            settings.UpdateHomeVisitRequests(true, service, HomeVisitChargePolicy.CreditedIfHired);
            settings.DismissHomeVisitAddressNotice(DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }
        await using (var context = database.CreateContext(plumber))
        {
            var saved = await context.TenantSettings.SingleAsync();
            Assert.Equal((true, (Guid?)service, HomeVisitChargePolicy.CreditedIfHired, true),
                (saved.HomeVisitRequestsEnabled, saved.HomeVisitServiceId, saved.HomeVisitChargePolicy,
                    saved.HomeVisitAddressNoticeDismissedAt.HasValue));
        }
        // Another business does not see them.
        await using (var foreign = database.CreateContext(shop))
            Assert.False((await foreign.TenantSettings.SingleAsync()).HomeVisitRequestsEnabled);

        foreach (var (sql, constraint) in new (FormattableString, string)[]
        {
            ($"UPDATE [TenantSettings] SET [HomeVisitChargePolicy] = 3 WHERE [TenantId] = {plumber}", "CK_TenantSettings_HomeVisitChargePolicy"),
            ($"UPDATE [TenantSettings] SET [HomeVisitChargePolicy] = -1 WHERE [TenantId] = {plumber}", "CK_TenantSettings_HomeVisitChargePolicy"),
            ($"UPDATE [Tenants] SET [DirectoryCategory] = 12 WHERE [Id] = {plumber}", "CK_Tenants_DirectoryCategory"),
        })
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(database, sql));
            Assert.Equal(547, error.Number);
            Assert.Contains(constraint, error.Message);
        }
        var nulls = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(database,
            $"UPDATE [TenantSettings] SET [HomeVisitRequestsEnabled] = NULL WHERE [TenantId] = {plumber}"));
        Assert.Equal(515, nulls.Number);
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopOnceAnOwnerDecided_AndElseTakeTheTradesBackToTheCategoryOfTheirGiro()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeMigration);
        var plumber = await SeedAsync(database, "Plomería", BusinessCategory.Services);
        var pestControl = await SeedAsync(database, "Fumigación", BusinessCategory.Services, BusinessType.Service);
        var gardener = await SeedAsync(database, "Jardinería", BusinessCategory.Other);
        await MigrateAsync(database, Migration);
        // A trade that signs up with this version: in 11 and receiving visits, which nobody audited.
        var locksmith = await SeedAsync(database, "Cerrajería", BusinessCategory.HomeRepairs, latest: true);

        // An owner who picked «Hogar y reparaciones» in Explorar stops it.
        var chose = await SeedAsync(database, "Abarrotes", BusinessCategory.HomeRepairs, ownerDecided: true, latest: true);
        await AssertStoppedAsync(database);
        await ExecuteAsync(database, $"UPDATE [Tenants] SET [DirectoryCategory] = 1 WHERE [Id] = {chose}");

        // So does an owner who changed how the business receives visits to quote.
        await using (var context = database.CreateContext())
        {
            context.Add(new AuditLog(plumber, "TenantSettings", plumber.ToString(), "HomeVisitRequestsUpdated", "owner"));
            await context.SaveChangesAsync();
        }
        await AssertStoppedAsync(database);
        await ExecuteAsync(database, $"DELETE FROM [AuditLogs] WHERE [Action] = N'HomeVisitRequestsUpdated'");

        await MigrateAsync(database, BeforeMigration);
        await using (var context = BeforeHomeVisitRequests.CreateContext(database))
        {
            Assert.Equal(BeforeMigration, await LatestMigrationAsync(context));
            Assert.Equal(0, await CountAsync(context, $"""
                SELECT COUNT(*) AS [Value] FROM sys.columns
                WHERE [object_id] = OBJECT_ID(N'dbo.TenantSettings') AND [name] LIKE N'HomeVisit%' AND [name] <> N'HomeVisitTravelMinutes'
                """));
            Assert.Equal("([DirectoryCategory]>=(0) AND [DirectoryCategory]<=(10))", await context.Database.SqlQuery<string>($"""
                SELECT [definition] AS [Value] FROM sys.check_constraints WHERE [name] = N'CK_Tenants_DirectoryCategory'
                """).SingleAsync());
            // Back to the category their giro gave: the trades of the list then to Servicios, the rest by their type.
            var categories = await context.Tenants.IgnoreQueryFilters().ToDictionaryAsync(t => t.Id, t => t.DirectoryCategory);
            Assert.Equal(BusinessCategory.Services, categories[plumber]);
            Assert.Equal(BusinessCategory.Services, categories[pestControl]);
            Assert.Equal(BusinessCategory.Other, categories[gardener]);
            Assert.Equal(BusinessCategory.Other, categories[locksmith]);
            Assert.Equal(BusinessCategory.Groceries, categories[chose]);
        }

        // Rolling back again does nothing, and forward again moves the trades as the first time.
        await MigrateAsync(database, BeforeMigration);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext();
        var again = await forward.Tenants.IgnoreQueryFilters().ToDictionaryAsync(t => t.Id, t => t.DirectoryCategory);
        // The locksmith signed up with this version is a shop now in Otros, which its giro gave: it moves too.
        Assert.Equal(
            (BusinessCategory.HomeRepairs, BusinessCategory.HomeRepairs, BusinessCategory.HomeRepairs, BusinessCategory.HomeRepairs),
            (again[plumber], again[pestControl], again[gardener], again[locksmith]));
    }

    private static async Task AssertStoppedAsync(SqlServerTestDatabase database)
    {
        var stopped = await Record.ExceptionAsync(() => MigrateAsync(database, BeforeMigration));
        Assert.True(stopped is SqlException { Number: 50049 }, stopped?.Message ?? "it was not stopped");
        await using var context = database.CreateContext();
        Assert.Equal(Migration, await LatestMigrationAsync(context));
    }

    /// <summary>
    /// A shop of the giro with its settings, in that category, as the version before this one stores it (or this one, with
    /// <paramref name="latest"/>, receiving visits as a trade does when it signs up); <paramref name="ownerDecided"/> audits
    /// its category as the owner's.
    /// </summary>
    private static async Task<Guid> SeedAsync(
        SqlServerTestDatabase database,
        string industry,
        BusinessCategory category,
        BusinessType type = BusinessType.Retail,
        bool ownerDecided = false,
        bool latest = false)
    {
        var slug = $"negocio-{Guid.NewGuid():N}"[..20];
        var tenant = new Tenant("Negocio", slug, type, $"{slug}@demo.com");
        var settings = new TenantSettings(tenant.Id, TenantCapability.Retail);
        await using (var context = latest ? database.CreateContext() : BeforeHomeVisitRequests.CreateContext(database))
        {
            if (latest)
                settings.StartReceivingHomeVisits();
            context.AddRange(tenant, settings);
            if (ownerDecided)
                context.Add(new AuditLog(tenant.Id, "Tenant", tenant.Id.ToString(), "DirectoryProfileUpdated", "owner"));
            await context.SaveChangesAsync();
        }
        await ExecuteAsync(database,
            $"UPDATE [Tenants] SET [Industry] = {industry}, [DirectoryCategory] = {(int)category} WHERE [Id] = {tenant.Id}");
        return tenant.Id;
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
        await context.Database.ExecuteSqlAsync(sql);
    }

    private static Task<int> CountAsync(ApplicationDbContext context, FormattableString sql)
        => context.Database.SqlQuery<int>(sql).SingleAsync();

    private static Task<string> LatestMigrationAsync(ApplicationDbContext context) =>
        context.Database.SqlQuery<string>($"""
            SELECT TOP (1) [MigrationId] AS [Value] FROM [__EFMigrationsHistory] ORDER BY [MigrationId] DESC
            """).SingleAsync();
}
