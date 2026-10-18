using FoodParkDigital.Domain.Branches.Entities;
using FoodParkDigital.Domain.Common.ValueObjects;
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
/// AddBranchLocation (Fase 24G, ADR 0029): the branches that exist are not on the map and the version deployed before keeps
/// saving them over the new columns; the database keeps both halves of a point or neither, in range and never 0, 0; and rolling back
/// stops with error 50022 once an owner put a branch on the map, and otherwise drops only the columns.
/// </summary>
public class BranchLocationMigrationSqlServerTests
{
    // The migration right before AddBranchLocation, where its rollback.sql takes the database.
    private const string BeforeLocation = "20261018081803_AddFavoriteBusinesses";
    private const string Migration = "20261018114538_AddBranchLocation";

    private static readonly GeoPoint Zocalo = new(19.043577m, -98.198626m);

    [SqlServerFact]
    public async Task ExistingBranches_ShouldNotBeOnTheMap_AndThePreviousVersionKeepsSavingThem()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeLocation);
        var tenant = NewTenant();
        var branch = Branch.CreateDefault(tenant.Id);
        await using (var before = BeforeBranchLocation.CreateContext(database))
        {
            before.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Food), branch);
            await before.SaveChangesAsync();
        }

        await MigrateAsync(database, Migration);

        await using (var context = database.CreateContext(tenant.Id))
        {
            var stored = await context.Branches.SingleAsync(b => b.Id == branch.Id);
            Assert.Null(stored.Location);
            stored.SetLocation(Zocalo);
            await context.SaveChangesAsync();
        }

        // The version deployed before does not know the columns: it saves the branch as always, and the point stays.
        await using (var previous = BeforeBranchLocation.CreateContext(database, tenant.Id))
        {
            var stored = await previous.Branches.SingleAsync(b => b.Id == branch.Id);
            stored.Update("Centro", stored.Code, new BranchContact(AddressLine1: "Av. Reforma 1"), stored.ServiceMode);
            await previous.SaveChangesAsync();
        }

        await using var check = database.CreateContext(tenant.Id);
        var saved = await check.Branches.SingleAsync(b => b.Id == branch.Id);
        Assert.Equal(("Centro", "Av. Reforma 1", Zocalo), (saved.Name, saved.AddressLine1, saved.Location));
    }

    [SqlServerFact]
    public async Task Database_ShouldRefuse_HalfAPoint_APointOffTheMap_AndZeroZero()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var tenant = NewTenant();
        var branch = Branch.CreateDefault(tenant.Id);
        await using (var seed = database.CreateContext())
        {
            seed.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Food), branch);
            await seed.SaveChangesAsync();
        }

        // Half a point, a latitude past the pole, a longitude past the antimeridian, and the 0, 0 of a device without a
        // position.
        foreach (var update in new[]
                 {
                     "UPDATE [Branches] SET [Latitude] = 19.043577 WHERE [Id] = {0}",
                     "UPDATE [Branches] SET [Latitude] = 91, [Longitude] = -98.198626 WHERE [Id] = {0}",
                     "UPDATE [Branches] SET [Latitude] = 19.043577, [Longitude] = -181 WHERE [Id] = {0}",
                     "UPDATE [Branches] SET [Latitude] = 0, [Longitude] = 0 WHERE [Id] = {0}"
                 })
        {
            await using var context = database.CreateContext();
            var error = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlRawAsync(update, branch.Id));
            Assert.Equal(547, error.Number);
            Assert.Contains("CK_Branches_Location", error.Message);
        }

        // Either half alone may be 0: the equator and the meridian of Greenwich have places.
        await using var check = database.CreateContext();
        await check.Database.ExecuteSqlRawAsync("UPDATE [Branches] SET [Latitude] = 0, [Longitude] = -78.45 WHERE [Id] = {0}", branch.Id);
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_OnceABranchIsOnTheMap_AndThenDropOnlyTheColumns()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var tenant = NewTenant();
        var branch = Branch.CreateDefault(tenant.Id);
        branch.SetLocation(Zocalo);
        await using (var seed = database.CreateContext())
        {
            seed.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Food), branch);
            await seed.SaveChangesAsync();
        }

        var error = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(database, BeforeLocation));
        Assert.Equal(50022, error.Number);
        Assert.Contains("Owners put branches on the map", error.Message);
        await using (var context = database.CreateContext())
        {
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            Assert.Equal(1, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [Branches] WHERE [Latitude] IS NOT NULL"));
            // The owner takes it off the map: no branch is left on it.
            await context.Database.ExecuteSqlRawAsync("UPDATE [Branches] SET [Latitude] = NULL, [Longitude] = NULL");
        }

        await MigrateAsync(database, BeforeLocation);
        await using (var context = BeforeBranchLocation.CreateContext(database))
        {
            Assert.Equal(BeforeLocation, await LatestMigrationAsync(context));
            Assert.Equal((0, 0, 1), (
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM sys.columns WHERE [object_id] = OBJECT_ID(N'dbo.Branches') AND [name] IN (N'Latitude', N'Longitude')"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM sys.check_constraints WHERE [name] = N'CK_Branches_Location'"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [Branches]")));
        }

        // Rolling back again does nothing, and forward again the columns are back, empty.
        await MigrateAsync(database, BeforeLocation);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext(tenant.Id);
        Assert.Null((await forward.Branches.SingleAsync(b => b.Id == branch.Id)).Location);
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
