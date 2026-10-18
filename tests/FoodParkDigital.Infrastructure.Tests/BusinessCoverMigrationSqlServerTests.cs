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
/// AddBusinessCover (Fase 24E, ADR 0026): the businesses that exist have no cover and the version deployed before keeps
/// saving them over the new column; rolling back stops with error 50020 once an owner uploaded a cover, and otherwise
/// drops only the column.
/// </summary>
public class BusinessCoverMigrationSqlServerTests
{
    // The migration right before AddBusinessCover, where its rollback.sql takes the database.
    private const string BeforeCover = "20261017225015_AddOnlineBooking";
    private const string Migration = "20261018032200_AddBusinessCover";

    [SqlServerFact]
    public async Task ExistingBusinesses_ShouldHaveNoCover_AndThePreviousVersionKeepsSavingThem()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeCover);
        var tenant = NewTenant();
        await using (var before = BeforeBusinessCover.CreateContext(database))
        {
            before.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Food));
            await before.SaveChangesAsync();
        }

        await MigrateAsync(database, Migration);

        await using (var context = database.CreateContext())
        {
            var stored = await context.Tenants.SingleAsync(t => t.Id == tenant.Id);
            Assert.Null(stored.CoverImageUrl);
            stored.SetCoverImage("/uploads/covers/demo/portada");
            await context.SaveChangesAsync();
        }

        // The version deployed before does not know the column: it saves the business as always, and the cover stays.
        await using (var previous = BeforeBusinessCover.CreateContext(database))
        {
            var stored = await previous.Tenants.SingleAsync(t => t.Id == tenant.Id);
            stored.Update("Café Luna", stored.ContactEmail, stored.ContactPhone, "/uploads/logos/demo/logo");
            await previous.SaveChangesAsync();
        }

        await using var check = database.CreateContext();
        var saved = await check.Tenants.SingleAsync(t => t.Id == tenant.Id);
        Assert.Equal(("Café Luna", "/uploads/logos/demo/logo", "/uploads/covers/demo/portada"),
            (saved.Name, saved.LogoUrl, saved.CoverImageUrl));
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_OnceAnOwnerUploadedACover_AndThenDropOnlyTheColumn()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var tenant = NewTenant();
        tenant.SetCoverImage("/uploads/covers/demo/portada");
        await using (var seed = database.CreateContext())
        {
            seed.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Food));
            await seed.SaveChangesAsync();
        }

        var error = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(database, BeforeCover));
        Assert.Equal(50020, error.Number);
        Assert.Contains("Owners uploaded menu covers", error.Message);
        await using (var context = database.CreateContext())
        {
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            Assert.Equal(1, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [Tenants] WHERE [CoverImageUrl] IS NOT NULL"));
            // The owner removes it: no cover is left.
            await context.Database.ExecuteSqlRawAsync("UPDATE [Tenants] SET [CoverImageUrl] = NULL");
        }

        await MigrateAsync(database, BeforeCover);
        await using (var context = database.CreateContext())
        {
            Assert.Equal(BeforeCover, await LatestMigrationAsync(context));
            Assert.Equal((0, 1), (
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM sys.columns WHERE [object_id] = OBJECT_ID(N'dbo.Tenants') AND [name] = N'CoverImageUrl'"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [Tenants]")));
        }

        // Rolling back again does nothing, and forward again the column is back, empty.
        await MigrateAsync(database, BeforeCover);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext();
        Assert.Null((await forward.Tenants.SingleAsync(t => t.Id == tenant.Id)).CoverImageUrl);
    }

    private static Tenant NewTenant()
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        return new Tenant($"Café {slug}", slug, BusinessType.Food, $"{slug}@demo.com");
    }

    /// <summary>Applies the migrations up to <paramref name="target"/>, as a deployment does.</summary>
    private static async Task MigrateAsync(SqlServerTestDatabase database, string target)
    {
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(target);
    }

    private static Task<string> LatestMigrationAsync(ApplicationDbContext context) =>
        context.Database.SqlQuery<string>($"""
            SELECT TOP (1) [MigrationId] AS [Value] FROM [__EFMigrationsHistory] ORDER BY [MigrationId] DESC
            """).SingleAsync();

    private static Task<int> CountAsync(ApplicationDbContext context, FormattableString sql) =>
        context.Database.SqlQuery<int>(sql).SingleAsync();
}
