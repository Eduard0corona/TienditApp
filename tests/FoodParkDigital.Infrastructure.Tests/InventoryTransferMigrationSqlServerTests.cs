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
/// AddInventoryTransfers (Fase 27B, ADR 0038): the transfers of each business, each with its folio, its key and its own two
/// movements; the checks refuse what the domain never writes; and rolling back stops with error 50031 once a business made
/// a transfer, and otherwise drops only the table.
/// </summary>
public class InventoryTransferMigrationSqlServerTests
{
    // The migration right before AddInventoryTransfers, where its rollback.sql takes the database.
    private const string BeforeTransfers = "20261021002749_AddInventoryCounts";
    private const string Migration = "20261021040954_AddInventoryTransfers";
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 18, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task Table_ShouldKeepTheTransfersOfEachBusiness_EachWithItsKeyAndItsOwnTwoMovements()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var tenant = await SeedTenantAsync(database);
        var other = await SeedTenantAsync(database);
        var shelf = await SeedShelfAsync(database, tenant.Id);
        var key = Guid.NewGuid();
        await using (var context = database.CreateContext(tenant.Id))
        {
            var (exit, entry) = Legs(tenant.Id, shelf, "TRF-20261021-0001", Guid.NewGuid());
            context.AddRange(exit, entry, new InventoryTransfer(Guid.NewGuid(), tenant.Id, "TRF-20261021-0001", key,
                shelf.Warehouse.Id, shelf.Store.Id, Now, "dueno", exit.Id, entry.Id, 1, 36m, "Para el fin de semana"));
            await context.SaveChangesAsync();
        }

        await using (var check = database.CreateContext(tenant.Id))
        {
            var transfer = await check.InventoryTransfers.SingleAsync();
            Assert.Equal(("TRF-20261021-0001", key, shelf.Warehouse.Id, shelf.Store.Id, 1, 36m, "Para el fin de semana"),
                (transfer.TransferNumber, transfer.IdempotencyKey, transfer.FromLocationId, transfer.ToLocationId, transfer.LineCount,
                    transfer.TotalCost, transfer.Notes));
            var legs = await check.InventoryMovements
                .Where(m => m.Id == transfer.OutMovementId || m.Id == transfer.InMovementId)
                .OrderBy(m => m.MovementType)
                .Select(m => new { m.MovementType, m.MovementReason })
                .ToListAsync();
            Assert.Equal([(InventoryMovementType.TransferIn, InventoryMovementReason.Transfer), (InventoryMovementType.TransferOut, InventoryMovementReason.Transfer)],
                legs.Select(l => (l.MovementType, l.MovementReason)));
        }
        await using (var elsewhere = database.CreateContext(other.Id))
            Assert.Empty(await elsewhere.InventoryTransfers.ToListAsync());

        // The same key again, or a movement of another transfer, is refused by the base too.
        await using var raw = database.CreateContext(tenant.Id);
        var (secondExit, secondEntry) = Legs(tenant.Id, shelf, "TRF-20261021-0002", Guid.NewGuid());
        raw.AddRange(secondExit, secondEntry, new InventoryTransfer(Guid.NewGuid(), tenant.Id, "TRF-20261021-0002", key,
            shelf.Store.Id, shelf.Warehouse.Id, Now, "dueno", secondExit.Id, secondEntry.Id, 1, 12m));
        var sameKey = await Assert.ThrowsAsync<DbUpdateException>(() => raw.SaveChangesAsync());
        Assert.Contains("IX_InventoryTransfers_TenantId_IdempotencyKey", sameKey.InnerException!.Message);
        raw.ChangeTracker.Clear();
        raw.AddRange(secondExit, secondEntry);
        await raw.SaveChangesAsync();
        var first = await raw.InventoryTransfers.AsNoTracking().SingleAsync();
        var sameExit = await Assert.ThrowsAsync<SqlException>(() => raw.Database.ExecuteSqlAsync($"""
            INSERT INTO [InventoryTransfers] ([Id], [TenantId], [TransferNumber], [IdempotencyKey], [FromLocationId], [ToLocationId],
                [TransferredAt], [TransferredBy], [OutMovementId], [InMovementId], [LineCount], [TotalCost], [CreatedAt])
            VALUES ({Guid.NewGuid()}, {tenant.Id}, N'TRF-20261021-0002', {Guid.NewGuid()}, {shelf.Store.Id}, {shelf.Warehouse.Id},
                SYSDATETIMEOFFSET(), N'dueno', {first.OutMovementId}, {secondEntry.Id}, 1, 12, SYSDATETIMEOFFSET())
            """));
        Assert.Equal(2601, sameExit.Number);
        Assert.Contains("IX_InventoryTransfers_OutMovementId", sameExit.Message);
    }

    [SqlServerFact]
    public async Task Checks_ShouldRefuse_WhatTheDomainNeverWrites_AndKeepItsLocationsAndMovements()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var tenant = await SeedTenantAsync(database);
        var shelf = await SeedShelfAsync(database, tenant.Id);
        await SeedTransferAsync(database, tenant.Id, shelf);

        (string Sql, string What)[] refusals =
        [
            ("UPDATE [InventoryTransfers] SET [ToLocationId] = [FromLocationId]", "the same location as origin and destination"),
            ("UPDATE [InventoryTransfers] SET [InMovementId] = [OutMovementId]", "one movement as exit and entry"),
            ("UPDATE [InventoryTransfers] SET [LineCount] = 0", "no items"),
            ("UPDATE [InventoryTransfers] SET [LineCount] = 201", "201 items"),
            ("UPDATE [InventoryTransfers] SET [TotalCost] = -1", "a negative value"),
            ($"UPDATE [InventoryTransfers] SET [FromLocationId] = '{Guid.NewGuid()}'", "a location that does not exist"),
            ($"UPDATE [InventoryTransfers] SET [OutMovementId] = '{Guid.NewGuid()}'", "a movement that does not exist"),
            ("DELETE FROM [InventoryMovements] WHERE [Id] IN (SELECT [InMovementId] FROM [InventoryTransfers])", "deleting a leg"),
            ("DELETE FROM [InventoryLocations] WHERE [Id] IN (SELECT [ToLocationId] FROM [InventoryTransfers])", "deleting a location"),
        ];
        await using var raw = database.CreateContext();
        foreach (var (sql, what) in refusals)
        {
            var refused = await Assert.ThrowsAsync<SqlException>(() => raw.Database.ExecuteSqlRawAsync(sql));
            Assert.True(refused.Number == 547, $"{what}: {refused.Message}");
        }
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_OnceATransferWasMade_AndThenDropOnlyTheTable()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var tenant = await SeedTenantAsync(database);
        var shelf = await SeedShelfAsync(database, tenant.Id);
        await SeedTransferAsync(database, tenant.Id, shelf);

        var made = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(database, BeforeTransfers));
        Assert.Equal(50031, made.Number);
        Assert.Contains("transfers between locations", made.Message);
        await using (var context = database.CreateContext())
        {
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            Assert.Equal(1, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [InventoryTransfers]"));
        }

        // Without transfers its legs stay, as plain movements.
        await ExecuteAsync(database, "DELETE FROM [InventoryTransfers]");
        await MigrateAsync(database, BeforeTransfers);
        await using (var context = database.CreateContext())
        {
            Assert.Equal(BeforeTransfers, await LatestMigrationAsync(context));
            Assert.Equal((0, 2, 2), (
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE [name] = N'InventoryTransfers'"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [InventoryLocations]"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [InventoryMovements]")));
        }

        // Rolling back again does nothing, and forward again the table is there, empty.
        await MigrateAsync(database, BeforeTransfers);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext(tenant.Id);
        Assert.Empty(await forward.InventoryTransfers.ToListAsync());
    }

    private sealed record Shelf(InventoryLocation Warehouse, InventoryLocation Store, SupplyItem Soda);

    private static (InventoryMovement Exit, InventoryMovement Entry) Legs(Guid tenantId, Shelf shelf, string folio, Guid transferId)
    {
        var exit = new InventoryMovement(tenantId, $"INV-{Guid.NewGuid():N}"[..20], InventoryMovementType.TransferOut,
            InventoryMovementReason.Transfer, Now, folio, $"TRF:{transferId:N}:OUT");
        exit.AddLine(tenantId, shelf.Soda.Id, shelf.Warehouse.Id, 3m, 12m);
        var entry = new InventoryMovement(tenantId, $"INV-{Guid.NewGuid():N}"[..20], InventoryMovementType.TransferIn,
            InventoryMovementReason.Transfer, Now, folio, $"TRF:{transferId:N}:IN");
        entry.AddLine(tenantId, shelf.Soda.Id, shelf.Store.Id, 3m, 12m);
        return (exit, entry);
    }

    private static async Task SeedTransferAsync(SqlServerTestDatabase database, Guid tenantId, Shelf shelf)
    {
        var id = Guid.NewGuid();
        var (exit, entry) = Legs(tenantId, shelf, "TRF-20261021-0001", id);
        await using var context = database.CreateContext(tenantId);
        context.AddRange(exit, entry, new InventoryTransfer(id, tenantId, "TRF-20261021-0001", Guid.NewGuid(),
            shelf.Warehouse.Id, shelf.Store.Id, Now, "dueno", exit.Id, entry.Id, 1, 36m));
        await context.SaveChangesAsync();
    }

    private static async Task<Shelf> SeedShelfAsync(SqlServerTestDatabase database, Guid tenantId)
    {
        var warehouse = new InventoryLocation(tenantId, "Bodega", "BOD", isDefault: true);
        var store = new InventoryLocation(tenantId, "Sucursal centro", "CEN");
        var soda = new SupplyItem(tenantId, "Refresco de cola 600 ml", UnitType.Unit, defaultCost: 12m);
        await using var context = database.CreateContext(tenantId);
        context.AddRange(warehouse, store, soda);
        await context.SaveChangesAsync();
        return new Shelf(warehouse, store, soda);
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
