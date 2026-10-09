using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// TenantSettings.Theme (Fase 16) only adds a column: every business has Nopal until its owner picks another, the
/// version deployed before saves its rows without the column (the default fills it), and rolling it back drops only
/// the choice.
/// </summary>
public class BusinessThemeSqlServerTests
{
    // The migration right before AddBusinessTheme, where its rollback.sql takes the database.
    private const string BeforeTheme = "20261009041644_StopMappingRequireOpenCashSession";

    [SqlServerFact]
    public async Task Theme_ShouldDefaultToNopal_AndRollBackWithoutTouchingTheRest()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database);
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant("Taquería", slug, BusinessType.Food, $"{slug}@demo.com");
        await using (var seed = database.CreateContext(tenant.Id))
        {
            seed.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Food));
            await seed.SaveChangesAsync();
        }

        await using (var context = database.CreateContext(tenant.Id))
        {
            // What lets the previous version keep saving settings: NOT NULL with the default of Nopal.
            Assert.Equal(1, await CountAsync(context, $"""
                SELECT COUNT(*) AS [Value] FROM sys.columns c
                INNER JOIN sys.default_constraints d ON d.[object_id] = c.[default_object_id]
                WHERE c.[object_id] = OBJECT_ID(N'dbo.TenantSettings') AND c.[name] = N'Theme'
                    AND c.[is_nullable] = 0 AND d.[definition] = N'((0))'
                """));
            var settings = await context.TenantSettings.SingleAsync(s => s.TenantId == tenant.Id);
            Assert.Equal(BusinessTheme.Nopal, settings.Theme);

            settings.SetTheme(BusinessTheme.Cempasuchil);
            settings.SetInsufficientStockPolicy(InsufficientStockPolicy.Warn);
            await context.SaveChangesAsync();
            Assert.Equal(1, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM TenantSettings WHERE TenantId = {tenant.Id} AND Theme = 5"));
        }

        // Back to the version before themes: the column goes, the rest of the settings stay.
        await MigrateAsync(database, BeforeTheme);
        await using (var context = database.CreateContext(tenant.Id))
        {
            Assert.Equal(0, await CountAsync(context,
                $"SELECT COUNT(*) AS [Value] FROM sys.columns WHERE [object_id] = OBJECT_ID(N'dbo.TenantSettings') AND [name] = N'Theme'"));
            Assert.Equal(1, await CountAsync(context,
                $"SELECT COUNT(*) AS [Value] FROM TenantSettings WHERE TenantId = {tenant.Id} AND InsufficientStockPolicy = 1"));
        }

        // Forward again: the business is back on Nopal until its owner picks a theme again.
        await MigrateAsync(database);
        await using (var context = database.CreateContext(tenant.Id))
        {
            var settings = await context.TenantSettings.AsNoTracking().SingleAsync(s => s.TenantId == tenant.Id);
            Assert.Equal(BusinessTheme.Nopal, settings.Theme);
            Assert.Equal(InsufficientStockPolicy.Warn, settings.InsufficientStockPolicy);
        }
    }

    private static async Task MigrateAsync(SqlServerTestDatabase database, string? target = null)
    {
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(target);
    }

    private static Task<int> CountAsync(ApplicationDbContext context, FormattableString sql) =>
        context.Database.SqlQuery<int>(sql).SingleAsync();
}
