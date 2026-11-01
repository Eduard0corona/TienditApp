using FoodParkDigital.Domain.Quotes;
using FoodParkDigital.Domain.Quotes.Entities;
using FoodParkDigital.Domain.Tenants;
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
/// AddQuotes (Fase 29C, ADR 0053): the quotes and how long a business keeps them valid. The version before it keeps saving
/// businesses over the new column, which stays at 15 days; rolling back stops with error 50051 once a business has a quote or
/// picked another validity, and otherwise drops only what it added, and a second run does nothing.
/// </summary>
public class QuoteMigrationSqlServerTests
{
    // The migration right before AddQuotes, where its rollback.sql takes the database.
    private const string BeforeMigration = "20261101002207_AddHomeVisitBooking";
    private const string Migration = "20261101055834_AddQuotes";
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 18, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task PreviousVersion_ShouldKeepSavingBusinesses_WithQuotesValidFifteenDays()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);

        // The app as deployed before this version signs up a business, unaware of the new column.
        Guid tenantId;
        await using (var before = BeforeQuotes.CreateContext(database))
        {
            var slug = Guid.NewGuid().ToString("N")[..10];
            var tenant = new Tenant($"Plomería {slug}", slug, BusinessType.Service, $"{slug}@demo.com");
            before.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Service | TenantCapability.Appointments));
            await before.SaveChangesAsync();
            tenantId = tenant.Id;
        }

        await using var context = database.CreateContext(tenantId);
        Assert.Equal(15, (await context.TenantSettings.SingleAsync()).QuoteValidityDays);
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_OnceThereIsAQuoteOrAnotherValidity_AndThenDropOnlyWhatItAdded()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var tenantId = await SeedAsync(database);
        await using (var context = database.CreateContext(tenantId))
        {
            // A draft alone: it would be lost too.
            context.Quotes.Add(Quote.Start(tenantId, Folios.QuoteNumber(1),
                new QuoteCustomer(null, "Valeria", "+525522107765", null), null, true, 15, Now));
            await context.SaveChangesAsync();
        }

        // The quote stops it alone; then, removed on purpose, a validity other than 15 days alone stops it too.
        foreach (var cleanUp in new[]
        {
            "DELETE FROM [Quotes]; UPDATE [TenantSettings] SET [QuoteValidityDays] = 30",
            "UPDATE [TenantSettings] SET [QuoteValidityDays] = 15",
        })
        {
            var stopped = await Record.ExceptionAsync(() => MigrateAsync(database, BeforeMigration));
            Assert.True(stopped is SqlException { Number: 50051 }, stopped?.Message ?? "it was not stopped");
            await using var context = database.CreateContext(tenantId);
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            await context.Database.ExecuteSqlRawAsync(cleanUp);
        }

        await MigrateAsync(database, BeforeMigration);
        await using (var context = database.CreateContext())
        {
            Assert.Equal(BeforeMigration, await LatestMigrationAsync(context));
            Assert.Equal(0, await context.Database.SqlQuery<int>($"""
                SELECT COUNT(*) AS [Value] FROM sys.objects
                WHERE [object_id] IN (OBJECT_ID(N'dbo.Quotes'), OBJECT_ID(N'dbo.QuoteLines'))
                """).SingleAsync());
            Assert.Equal(0, await context.Database.SqlQuery<int>($"""
                SELECT COUNT(*) AS [Value] FROM sys.columns
                WHERE [object_id] = OBJECT_ID(N'dbo.TenantSettings') AND [name] = N'QuoteValidityDays'
                """).SingleAsync());
            Assert.Equal(0, await context.Database.SqlQuery<int>(
                $"SELECT COUNT(*) AS [Value] FROM sys.check_constraints WHERE [name] = N'CK_TenantSettings_QuoteValidityDays'").SingleAsync());
            // The business stays.
            Assert.Equal(1, await context.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM [TenantSettings]").SingleAsync());
        }

        // Rolling back again does nothing, and forward again the column is back at 15 days.
        await MigrateAsync(database, BeforeMigration);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext(tenantId);
        Assert.Equal((15, 0), ((await forward.TenantSettings.SingleAsync()).QuoteValidityDays, await forward.Quotes.CountAsync()));
    }

    private static async Task<Guid> SeedAsync(SqlServerTestDatabase database)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant($"Plomería {slug}", slug, BusinessType.Service, $"{slug}@demo.com");
        await using var seed = database.CreateContext(tenant.Id);
        seed.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Service | TenantCapability.Appointments));
        await seed.SaveChangesAsync();
        return tenant.Id;
    }

    private static async Task MigrateAsync(SqlServerTestDatabase database, string target)
    {
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(target);
    }

    private static Task<string> LatestMigrationAsync(ApplicationDbContext context) =>
        context.Database.SqlQuery<string>($"""
            SELECT TOP (1) [MigrationId] AS [Value] FROM [__EFMigrationsHistory] ORDER BY [MigrationId] DESC
            """).SingleAsync();
}
