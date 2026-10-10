using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// TenantSettings.OpeningHours (Fase 19) only adds a nullable column: no business has hours until its owner sets
/// them, the version deployed before saves its rows without it, and rolling it back drops only the hours.
/// </summary>
public class OpeningHoursSqlServerTests
{
    // The migration right before AddOpeningHours, where its rollback.sql takes the database.
    private const string BeforeHours = "20261009071957_DropRequireOpenCashSession";

    [SqlServerFact]
    public async Task Hours_ShouldStartEmpty_RoundTripAsJson_AndRollBackWithoutTouchingTheRest()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database);
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant("Ferretería", slug, BusinessType.Retail, $"{slug}@demo.com");
        await using (var seed = database.CreateContext(tenant.Id))
        {
            seed.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Retail));
            await seed.SaveChangesAsync();
        }

        var hours = OpeningHours.Create(
        [
            new OpeningInterval(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(14, 0)),
            new OpeningInterval(DayOfWeek.Monday, new TimeOnly(16, 0), new TimeOnly(20, 0)),
            new OpeningInterval(DayOfWeek.Friday, new TimeOnly(18, 0), new TimeOnly(2, 0)),
        ]);
        await using (var context = database.CreateContext(tenant.Id))
        {
            // What lets the previous version keep saving settings: a column that takes nulls.
            Assert.Equal(1, await CountAsync(context, $"""
                SELECT COUNT(*) AS [Value] FROM sys.columns
                WHERE [object_id] = OBJECT_ID(N'dbo.TenantSettings') AND [name] = N'OpeningHours'
                    AND [is_nullable] = 1 AND [max_length] = 2000
                """));
            var settings = await context.TenantSettings.SingleAsync(s => s.TenantId == tenant.Id);
            Assert.Null(settings.OpeningHours);

            settings.SetOpeningHours(hours);
            settings.SetInsufficientStockPolicy(InsufficientStockPolicy.Warn);
            await context.SaveChangesAsync();
            // One JSON list, Sunday as day 0, the next-day closing as written.
            Assert.Equal(1, await CountAsync(context, $$"""
                SELECT COUNT(*) AS [Value] FROM TenantSettings
                WHERE TenantId = {{tenant.Id}}
                    AND OpeningHours = N'[{"day":1,"opens":"09:00","closes":"14:00"},{"day":1,"opens":"16:00","closes":"20:00"},{"day":5,"opens":"18:00","closes":"02:00"}]'
                """));
        }

        await using (var context = database.CreateContext(tenant.Id))
        {
            var settings = await context.TenantSettings.AsNoTracking().SingleAsync(s => s.TenantId == tenant.Id);
            Assert.Equal(hours, settings.OpeningHours);
        }

        // Back to the version before hours: the column goes, the rest of the settings stay.
        await MigrateAsync(database, BeforeHours);
        await using (var context = database.CreateContext(tenant.Id))
        {
            Assert.Equal(0, await CountAsync(context,
                $"SELECT COUNT(*) AS [Value] FROM sys.columns WHERE [object_id] = OBJECT_ID(N'dbo.TenantSettings') AND [name] = N'OpeningHours'"));
            Assert.Equal(1, await CountAsync(context,
                $"SELECT COUNT(*) AS [Value] FROM TenantSettings WHERE TenantId = {tenant.Id} AND InsufficientStockPolicy = 1"));
        }

        // Forward again: the business has no hours until its owner sets them again.
        await MigrateAsync(database);
        await using (var context = database.CreateContext(tenant.Id))
        {
            var settings = await context.TenantSettings.AsNoTracking().SingleAsync(s => s.TenantId == tenant.Id);
            Assert.Null(settings.OpeningHours);
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
