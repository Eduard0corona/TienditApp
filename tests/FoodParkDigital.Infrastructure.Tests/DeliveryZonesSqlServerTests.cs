using FoodParkDigital.Domain.Delivery;
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
/// The tables of the delivery zones (Fase 22B): a zone keeps its postal codes and cents in its business, a postal code is
/// in one zone of a business at most (another business can have it too), a saved zone keeps the rows of the postal codes
/// it still has, deleting a zone takes its postal codes, the checks keep every row one the domain could write, and
/// rolling back stops while zones are saved and otherwise drops only the two tables.
/// </summary>
public class DeliveryZonesSqlServerTests
{
    private const string BeforeDeliveryZones = "20261012204959_AddBranchServiceMode";
    private const string Migration = "20261013002425_AddDeliveryZones";

    [SqlServerFact]
    public async Task Tables_ShouldHaveTheirIndexesAndKeys()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateContext();

        var indexes = await context.Database.SqlQuery<IndexRow>($"""
            SELECT [name] AS [Name], CAST([is_unique] AS bit) AS [IsUnique]
            FROM sys.indexes
            WHERE [object_id] IN (OBJECT_ID(N'dbo.DeliveryZones'), OBJECT_ID(N'dbo.DeliveryZonePostalCodes')) AND [type] > 0
            """).ToListAsync();
        Assert.True(Assert.Single(indexes, index => index.Name == "IX_DeliveryZonePostalCodes_TenantId_PostalCode").IsUnique);
        Assert.False(Assert.Single(indexes, index => index.Name == "IX_DeliveryZonePostalCodes_DeliveryZoneId").IsUnique);
        Assert.False(Assert.Single(indexes, index => index.Name == "IX_DeliveryZones_TenantId_Name").IsUnique);
        Assert.Equal(5, indexes.Count);

        var onDelete = await context.Database.SqlQuery<string>($"""
            SELECT [delete_referential_action_desc] AS [Value] FROM sys.foreign_keys
            WHERE [name] = N'FK_DeliveryZonePostalCodes_DeliveryZones_DeliveryZoneId'
            """).SingleAsync();
        Assert.Equal("CASCADE", onDelete);
    }

    [SqlServerFact]
    public async Task Zone_ShouldBeStoredWithItsPostalCodesAndCents_InItsBusinessOnly()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, other) = await SeedAsync(database);
        await using (var context = database.CreateContext(tenant.Id))
        {
            context.DeliveryZones.Add(new DeliveryZone(tenant.Id, new DeliveryZoneFields(
                "Centro", 35.50m, 149.99m, 399.99m, ["72005", "72000"])));
            await context.SaveChangesAsync();
        }

        await using (var check = database.CreateContext(tenant.Id))
        {
            var zone = await check.DeliveryZones.Include(z => z.PostalCodes).SingleAsync();
            Assert.Equal((35.50m, 149.99m, 399.99m), (zone.Fee, zone.MinimumOrder, zone.FreeDeliveryFrom));
            Assert.Equal(["72000", "72005"], zone.PostalCodes.Select(code => code.PostalCode));
        }
        await using (var neighbor = database.CreateContext(other.Id))
        {
            Assert.Empty(await neighbor.DeliveryZones.ToListAsync());
            Assert.Empty(await neighbor.DeliveryZonePostalCodes.ToListAsync());
        }
    }

    [SqlServerFact]
    public async Task PostalCode_ShouldBeInOneZoneOfTheBusiness_WhileAnotherBusinessCanHaveItToo()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, other) = await SeedAsync(database);
        await using (var context = database.CreateContext(tenant.Id))
        {
            context.DeliveryZones.Add(new DeliveryZone(tenant.Id, Fields("Centro", "72000")));
            await context.SaveChangesAsync();
        }

        await using (var context = database.CreateContext(tenant.Id))
        {
            context.DeliveryZones.Add(new DeliveryZone(tenant.Id, Fields("Angelópolis", "72830", "72000")));
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            Assert.Equal(2601, Assert.IsType<SqlException>(error.InnerException).Number);
        }

        await using (var neighbor = database.CreateContext(other.Id))
        {
            neighbor.DeliveryZones.Add(new DeliveryZone(other.Id, Fields("Centro", "72000")));
            await neighbor.SaveChangesAsync();
        }
    }

    [SqlServerFact]
    public async Task SavedZone_ShouldKeepTheRowsOfThePostalCodesItStillHas_AndDeletingItTakesThemAll()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        Guid zoneId;
        Guid keptId;
        await using (var context = database.CreateContext(tenant.Id))
        {
            var zone = new DeliveryZone(tenant.Id, Fields("Centro", "72000", "72005"));
            context.DeliveryZones.Add(zone);
            await context.SaveChangesAsync();
            zoneId = zone.Id;
            keptId = zone.PostalCodes.Single(code => code.PostalCode == "72000").Id;
        }

        await using (var context = database.CreateContext(tenant.Id))
        {
            var zone = await context.DeliveryZones.Include(z => z.PostalCodes).SingleAsync(z => z.Id == zoneId);
            zone.Update(Fields("Centro", "72000", "72010"));
            await context.SaveChangesAsync();
        }

        await using (var check = database.CreateContext(tenant.Id))
        {
            var codes = await check.DeliveryZonePostalCodes.OrderBy(code => code.PostalCode).ToListAsync();
            Assert.Equal(["72000", "72010"], codes.Select(code => code.PostalCode));
            Assert.Equal(keptId, codes[0].Id);

            check.DeliveryZones.Remove(await check.DeliveryZones.Include(z => z.PostalCodes).SingleAsync());
            await check.SaveChangesAsync();
        }

        await using var after = database.CreateContext(tenant.Id);
        Assert.Empty(await after.DeliveryZonePostalCodes.ToListAsync());
    }

    [SqlServerFact]
    public async Task Checks_ShouldRefuse_ARowTheDomainNeverWrites()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        (decimal Fee, decimal Minimum, decimal? FreeFrom, int Revision, string PostalCode)[] rows =
        [
            (-1m, 0m, null, 1, "72000"),
            (10_000.01m, 0m, null, 1, "72000"),
            (35m, -0.01m, null, 1, "72000"),
            (35m, 100_000.01m, null, 1, "72000"),
            (35m, 0m, 0m, 1, "72000"),
            (35m, 0m, 100_000.01m, 1, "72000"),
            (0m, 0m, 400m, 1, "72000"),
            (35m, 0m, null, 0, "72000"),
            (35m, 0m, null, 1, "7200"),
            (35m, 0m, null, 1, "7200a"),
            (35m, 0m, null, 1, "7200 "),
        ];
        await using var context = database.CreateContext();

        foreach (var row in rows)
        {
            var zoneId = Guid.NewGuid();
            var error = await Assert.ThrowsAsync<SqlException>(async () =>
            {
                await context.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO [DeliveryZones] ([Id], [TenantId], [Name], [Fee], [MinimumOrder], [FreeDeliveryFrom], [IsActive], [Revision], [CreatedAt])
                    VALUES ({zoneId}, {tenant.Id}, N'Centro', {row.Fee}, {row.Minimum}, {row.FreeFrom}, 1, {row.Revision}, SYSDATETIMEOFFSET())
                    """);
                await context.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO [DeliveryZonePostalCodes] ([Id], [TenantId], [DeliveryZoneId], [PostalCode])
                    VALUES ({Guid.NewGuid()}, {tenant.Id}, {zoneId}, {row.PostalCode})
                    """);
            });
            Assert.True(error.Number == 547, $"{row}: error {error.Number}");
        }
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_WhileZonesAreSaved_AndThenDropOnlyTheTwoTables()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var (tenant, _) = await SeedAsync(database);
        await using (var context = database.CreateContext(tenant.Id))
        {
            context.DeliveryZones.Add(new DeliveryZone(tenant.Id, Fields("Centro", "72000")));
            await context.SaveChangesAsync();
        }

        var error = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(database, BeforeDeliveryZones));
        Assert.Equal(50012, error.Number);
        Assert.Contains("delivery zones saved", error.Message);
        await using (var context = database.CreateContext(tenant.Id))
        {
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            var zone = await context.DeliveryZones.Include(z => z.PostalCodes).SingleAsync();
            Assert.Single(zone.PostalCodes);

            // Deleted on purpose, the rollback goes on.
            context.DeliveryZones.Remove(zone);
            await context.SaveChangesAsync();
        }

        await MigrateAsync(database, BeforeDeliveryZones);
        await using (var context = database.CreateContext())
        {
            Assert.Equal(BeforeDeliveryZones, await LatestMigrationAsync(context));
            Assert.Equal(0, await context.Database.SqlQuery<int>($"""
                SELECT COUNT(*) AS [Value] FROM sys.tables WHERE [name] IN (N'DeliveryZones', N'DeliveryZonePostalCodes')
                """).SingleAsync());
            Assert.Equal(2, await context.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM [Tenants]").SingleAsync());
        }
        // Rolling back again does nothing, and forward again the tables are back, empty.
        await MigrateAsync(database, BeforeDeliveryZones);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext(tenant.Id);
        Assert.Empty(await forward.DeliveryZones.ToListAsync());
    }

    private static DeliveryZoneFields Fields(string name, params string[] postalCodes)
        => new(name, 35m, 0m, null, postalCodes);

    private static async Task<(Tenant Tenant, Tenant Other)> SeedAsync(SqlServerTestDatabase database)
    {
        var tenant = NewTenant("Cocina de Lupita");
        var other = NewTenant("Fonda vecina");
        await using var seed = database.CreateContext();
        seed.AddRange(tenant, other, new TenantSettings(tenant.Id, TenantCapability.Food), new TenantSettings(other.Id, TenantCapability.Food));
        await seed.SaveChangesAsync();
        return (tenant, other);
    }

    private static Tenant NewTenant(string name)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        return new Tenant(name, slug, BusinessType.Food, $"{slug}@demo.com");
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

    private sealed record IndexRow(string Name, bool IsUnique);
}
