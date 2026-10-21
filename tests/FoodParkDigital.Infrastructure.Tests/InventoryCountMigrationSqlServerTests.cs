using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Inventory.Enums;
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
/// AddInventoryCounts (Fase 27A, ADR 0037): the physical counts of each business, one open per location, each item once
/// and counted with what the system had; the checks refuse what the domain never writes; and rolling back stops with
/// error 50030 once a business started a count, and otherwise drops only the two tables.
/// </summary>
public class InventoryCountMigrationSqlServerTests
{
    // The migration right before AddInventoryCounts, where its rollback.sql takes the database.
    private const string BeforeCounts = "20261020145628_AddCustomerBalance";
    private const string Migration = "20261021002749_AddInventoryCounts";
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 18, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task Tables_ShouldKeepTheCountsOfEachBusiness_OneOpenPerLocation_EachItemOnce()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var tenant = await SeedTenantAsync(database);
        var other = await SeedTenantAsync(database);
        var (location, soda, chips) = await SeedShelfAsync(database, tenant.Id);
        Guid countId;
        await using (var context = database.CreateContext(tenant.Id))
        {
            var count = new InventoryCount(tenant.Id, "CNT-20261021-0001", location.Id, Now, "dueno", "Bebidas", "Cierre de mes");
            var sodaLine = count.AddLine(soda.Id);
            var chipsLine = count.AddLine(chips.Id);
            count.Record(sodaLine, 22m, 24m, UnitType.Unit, Now, "cajero", "Dos latas abolladas");
            countId = count.Id;
            context.AddRange(count, sodaLine, chipsLine);
            await context.SaveChangesAsync();
        }

        await using (var check = database.CreateContext(tenant.Id))
        {
            var count = await check.InventoryCounts.SingleAsync();
            Assert.Equal((InventoryCountStatus.InProgress, 2, 1, 3, "Bebidas"),
                (count.Status, count.LineCount, count.CountedLineCount, count.Revision, count.CategoryName));
            var lines = await check.InventoryCountLines.Where(l => l.CountId == countId).OrderBy(l => l.CountedQuantity).ToListAsync();
            Assert.Equal([null, 22m], lines.Select(l => l.CountedQuantity));
            Assert.Equal(-2m, lines[1].Difference);
        }
        await using (var elsewhere = database.CreateContext(other.Id))
        {
            Assert.Empty(await elsewhere.InventoryCounts.ToListAsync());
            Assert.Empty(await elsewhere.InventoryCountLines.ToListAsync());
        }

        // A second open count of the location, or the same item twice, is refused by the base too.
        await using var raw = database.CreateContext(tenant.Id);
        raw.Add(new InventoryCount(tenant.Id, "CNT-20261021-0002", location.Id, Now, "cajero"));
        var secondOpen = await Assert.ThrowsAsync<DbUpdateException>(() => raw.SaveChangesAsync());
        Assert.Contains("IX_InventoryCounts_TenantId_InventoryLocationId_Open", secondOpen.InnerException!.Message);
        raw.ChangeTracker.Clear();
        var twice = await Assert.ThrowsAsync<SqlException>(() => raw.Database.ExecuteSqlAsync($"""
            INSERT INTO [InventoryCountLines] ([Id], [TenantId], [CountId], [SupplyItemId], [CreatedAt])
            VALUES ({Guid.NewGuid()}, {tenant.Id}, {countId}, {soda.Id}, SYSDATETIMEOFFSET())
            """));
        Assert.Equal(2601, twice.Number);
        Assert.Contains("IX_InventoryCountLines_CountId_SupplyItemId", twice.Message);

        // Once posted, the location takes a new count.
        var posted = await raw.InventoryCounts.SingleAsync();
        posted.Post(Now.AddHours(1), "dueno", null, null);
        raw.Add(new InventoryCount(tenant.Id, "CNT-20261021-0002", location.Id, Now.AddHours(2), "cajero"));
        await raw.SaveChangesAsync();
        Assert.Equal(1, await raw.InventoryCounts.CountAsync(c => c.Status == InventoryCountStatus.InProgress));
    }

    [SqlServerFact]
    public async Task Checks_ShouldRefuse_WhatTheDomainNeverWrites()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var tenant = await SeedTenantAsync(database);
        var (location, soda, chips) = await SeedShelfAsync(database, tenant.Id);
        await using (var context = database.CreateContext(tenant.Id))
        {
            var count = new InventoryCount(tenant.Id, "CNT-20261021-0001", location.Id, Now, "dueno");
            var sodaLine = count.AddLine(soda.Id);
            count.Record(sodaLine, 22m, 24m, UnitType.Unit, Now, "cajero");
            context.AddRange(count, sodaLine, count.AddLine(chips.Id));
            await context.SaveChangesAsync();
        }

        (string Sql, string What)[] refusals =
        [
            ("UPDATE [InventoryCounts] SET [Status] = 2", "posted without when and who"),
            ("UPDATE [InventoryCounts] SET [Status] = 3, [CancelledAt] = SYSDATETIMEOFFSET()", "cancelled without who"),
            ("UPDATE [InventoryCounts] SET [PostedAt] = SYSDATETIMEOFFSET(), [PostedBy] = N'dueno'", "open and posted"),
            ("UPDATE [InventoryCounts] SET [Status] = 4", "a state that is not one"),
            ("UPDATE [InventoryCounts] SET [CountedLineCount] = 3", "more counted than items"),
            ("UPDATE [InventoryCountLines] SET [CountedQuantity] = -1 WHERE [CountedQuantity] IS NOT NULL", "a negative count"),
            ("UPDATE [InventoryCountLines] SET [SystemQuantity] = NULL WHERE [CountedQuantity] IS NOT NULL", "counted without what the system had"),
            ("UPDATE [InventoryCountLines] SET [CountedBy] = NULL WHERE [CountedQuantity] IS NOT NULL", "counted without who"),
            ("UPDATE [InventoryCountLines] SET [SystemQuantity] = 3 WHERE [CountedQuantity] IS NULL", "what the system had without a count"),
            ($"UPDATE [InventoryCountLines] SET [SupplyItemId] = '{Guid.NewGuid()}'", "an item that does not exist"),
            ($"UPDATE [InventoryCounts] SET [InventoryLocationId] = '{Guid.NewGuid()}'", "a location that does not exist"),
        ];
        await using var raw = database.CreateContext();
        foreach (var (sql, what) in refusals)
        {
            var refused = await Assert.ThrowsAsync<SqlException>(() => raw.Database.ExecuteSqlRawAsync(sql));
            Assert.True(refused.Number == 547, $"{what}: {refused.Message}");
        }
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_OnceACountWasStarted_AndThenDropOnlyTheTables()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var tenant = await SeedTenantAsync(database);
        var (location, soda, _) = await SeedShelfAsync(database, tenant.Id);
        await using (var context = database.CreateContext(tenant.Id))
        {
            var count = new InventoryCount(tenant.Id, "CNT-20261021-0001", location.Id, Now, "dueno");
            context.AddRange(count, count.AddLine(soda.Id));
            await context.SaveChangesAsync();
        }

        // Open, posted or cancelled, a count stops it.
        (string State, string Sql)[] states =
        [
            ("open", "SELECT 1"),
            ("posted", "UPDATE [InventoryCountLines] SET [CountedQuantity] = 22, [SystemQuantity] = 24, [CountedAt] = SYSDATETIMEOFFSET(), [CountedBy] = N'cajero'; UPDATE [InventoryCounts] SET [Status] = 2, [CountedLineCount] = 1, [PostedAt] = SYSDATETIMEOFFSET(), [PostedBy] = N'dueno'"),
            ("cancelled", "UPDATE [InventoryCounts] SET [Status] = 3, [PostedAt] = NULL, [PostedBy] = NULL, [CancelledAt] = SYSDATETIMEOFFSET(), [CancelledBy] = N'dueno'"),
        ];
        foreach (var (state, sql) in states)
        {
            await ExecuteAsync(database, sql);
            var started = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(database, BeforeCounts));
            Assert.True(started.Number == 50030, $"{state}: {started.Message}");
            Assert.Contains("physical counts", started.Message);
            await using var context = database.CreateContext();
            Assert.Equal(Migration, await LatestMigrationAsync(context));
        }

        await ExecuteAsync(database, "DELETE FROM [InventoryCountLines]; DELETE FROM [InventoryCounts]");
        await MigrateAsync(database, BeforeCounts);
        await using (var context = database.CreateContext())
        {
            Assert.Equal(BeforeCounts, await LatestMigrationAsync(context));
            Assert.Equal((0, 1, 2), (
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE [name] IN (N'InventoryCounts', N'InventoryCountLines')"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [InventoryLocations]"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [SupplyItems]")));
        }

        // Rolling back again does nothing, and forward again the tables are there, empty.
        await MigrateAsync(database, BeforeCounts);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext(tenant.Id);
        Assert.Empty(await forward.InventoryCounts.ToListAsync());
        Assert.Empty(await forward.InventoryCountLines.ToListAsync());
    }

    private static async Task<(InventoryLocation Location, SupplyItem Soda, SupplyItem Chips)> SeedShelfAsync(
        SqlServerTestDatabase database, Guid tenantId)
    {
        var location = new InventoryLocation(tenantId, "Bodega", "BOD", isDefault: true);
        var soda = new SupplyItem(tenantId, "Refresco de cola 600 ml", UnitType.Unit, defaultCost: 12m);
        var chips = new SupplyItem(tenantId, "Papas 45 g", UnitType.Unit, defaultCost: 9m);
        await using var context = database.CreateContext(tenantId);
        context.AddRange(location, soda, chips);
        await context.SaveChangesAsync();
        return (location, soda, chips);
    }

    private static async Task<Tenant> SeedTenantAsync(SqlServerTestDatabase database)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant($"Tienda {slug}", slug, BusinessType.Retail, $"{slug}@demo.com");
        await using var seed = database.CreateContext();
        seed.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Retail));
        await seed.SaveChangesAsync();
        return tenant;
    }

    /// <summary>Applies the migrations up to <paramref name="target"/>, as a deployment does.</summary>
    private static async Task MigrateAsync(SqlServerTestDatabase database, string target)
    {
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(target);
    }

    private static async Task ExecuteAsync(SqlServerTestDatabase database, string sql)
    {
        await using var context = database.CreateContext();
        await context.Database.ExecuteSqlRawAsync(sql);
    }

    private static Task<string> LatestMigrationAsync(ApplicationDbContext context) =>
        context.Database.SqlQuery<string>($"""
            SELECT TOP (1) [MigrationId] AS [Value] FROM [__EFMigrationsHistory] ORDER BY [MigrationId] DESC
            """).SingleAsync();

    private static Task<int> CountAsync(ApplicationDbContext context, FormattableString sql) =>
        context.Database.SqlQuery<int>(sql).SingleAsync();
}
