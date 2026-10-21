using FoodParkDigital.Application.Common.Behaviors;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Inventory;
using FoodParkDigital.Application.Inventory.Transfers;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Inventory.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence.Interceptors;
using FoodParkDigital.Infrastructure.Services;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// Transferencias (Fase 27B, ADR 0038) in SQL Server set up as in production: the lock of the locations keeps transfers
/// made at once from the same origin from taking more than there is, transfers both ways at once take turns without
/// deadlocking, the same transfer sent twice at once moves the stock once (also one key sent at once for two pairs of
/// locations), and the folios never repeat.
/// </summary>
public class InventoryTransferSqlServerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 18, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task Transfers_FromTheSameOrigin_AtOnce_ShouldNeverTakeMoreThanThereIs()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var shop = await Shop.OpenAsync(database, extraLocations: 4);

        var transfers = await Task.WhenAll(shop.Locations.Skip(1).Select(to => shop.SendAsync(shop.Warehouse, to, 8m)));

        Assert.Equal(3, transfers.Count(t => t.IsSuccess));
        Assert.Equal("No hay suficiente en Bodega: Refresco de cola 600 ml (hay 0).", Assert.Single(transfers, t => t.IsFailure).Error);
        Assert.Equal(["TRF-20261021-0001", "TRF-20261021-0002", "TRF-20261021-0003"],
            transfers.Where(t => t.IsSuccess).Select(t => t.Value.TransferNumber).Order());
        await using var context = database.CreateContext(shop.TenantId);
        var sodas = context.InventoryStocks.Where(s => s.SupplyItemId == shop.Soda);
        Assert.Equal(0m, await sodas.Where(s => s.InventoryLocationId == shop.Warehouse).Select(s => s.OnHandQuantity).SingleAsync());
        Assert.Equal(24m, await sodas.SumAsync(s => s.OnHandQuantity));
        Assert.Equal(6, await context.InventoryMovements.CountAsync(m => m.MovementReason == InventoryMovementReason.Transfer));
    }

    [SqlServerFact]
    public async Task Transfers_BothWays_AtOnce_ShouldAllBeMade_WithoutDeadlocking()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var shop = await Shop.OpenAsync(database, extraLocations: 1);
        var store = shop.Locations[1];
        Assert.True((await shop.SendAsync(shop.Warehouse, store, 12m)).IsSuccess);

        var transfers = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            i % 2 == 0 ? shop.SendAsync(shop.Warehouse, store, 1m) : shop.SendAsync(store, shop.Warehouse, 2m)));

        Assert.All(transfers, t => Assert.True(t.IsSuccess, t.Error));
        await using var context = database.CreateContext(shop.TenantId);
        // 12 + 3 - 6 at the store; 24 in all.
        var sodas = context.InventoryStocks.Where(s => s.SupplyItemId == shop.Soda);
        Assert.Equal(9m, await sodas.Where(s => s.InventoryLocationId == store).Select(s => s.OnHandQuantity).SingleAsync());
        Assert.Equal(24m, await sodas.SumAsync(s => s.OnHandQuantity));
        Assert.Equal(7, await context.InventoryTransfers.CountAsync());
    }

    [SqlServerFact]
    public async Task TheSameTransfer_SentTwiceAtOnce_ShouldMoveTheStockOnce()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var shop = await Shop.OpenAsync(database, extraLocations: 1);
        var command = shop.Command(shop.Warehouse, shop.Locations[1], 5m);

        var sent = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => shop.SendAsync(command)));

        Assert.All(sent, s => Assert.True(s.IsSuccess, s.Error));
        Assert.Single(sent.Select(s => s.Value.Id).Distinct());
        await using var context = database.CreateContext(shop.TenantId);
        Assert.Equal(1, await context.InventoryTransfers.CountAsync());
        Assert.Equal([5m, 19m], (await context.InventoryStocks.Where(s => s.SupplyItemId == shop.Soda).Select(s => s.OnHandQuantity).ToListAsync()).Order());
    }

    [SqlServerFact]
    public async Task OneKey_SentAtOnce_ForTwoPairsOfLocations_ShouldMakeOneTransfer()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var shop = await Shop.OpenAsync(database, extraLocations: 3);
        Assert.True((await shop.SendAsync(shop.Warehouse, shop.Locations[2], 10m)).IsSuccess);
        var first = shop.Command(shop.Warehouse, shop.Locations[1], 5m);
        // Four other locations, so neither waits for the other's lock: the counter of the folio puts them in line and the
        // unique index of the key refuses the second, which is undone whole.
        var second = first with { FromLocationId = shop.Locations[2], ToLocationId = shop.Locations[3] };

        var sent = await Task.WhenAll(shop.SendAsync(first), shop.SendAsync(second));

        Assert.Single(sent, s => s.IsSuccess);
        Assert.Equal(CreateInventoryTransferCommandHandler.KeyUsedMessage, Assert.Single(sent, s => s.IsFailure).Error);
        await using var context = database.CreateContext(shop.TenantId);
        Assert.Equal(2, await context.InventoryTransfers.CountAsync());
        Assert.Equal(4, await context.InventoryMovements.CountAsync(m => m.MovementReason == InventoryMovementReason.Transfer));
        var sodas = await context.InventoryStocks.Where(s => s.SupplyItemId == shop.Soda)
            .ToDictionaryAsync(s => s.InventoryLocationId, s => s.OnHandQuantity);
        var moved = sent.Single(s => s.IsSuccess).Value;
        Assert.Equal(24m, sodas.Values.Sum());
        Assert.Equal(moved.ToLocationId == shop.Locations[1] ? (9m, 5m, 10m) : (14m, 0m, 5m),
            (sodas[shop.Warehouse], sodas.GetValueOrDefault(shop.Locations[1]), sodas[shop.Locations[2]]));
    }

    [SqlServerFact]
    public async Task ATransfer_ShouldBeReadByTheDatabase_WithBothLegsAndWhatEachLocationHad()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var shop = await Shop.OpenAsync(database, extraLocations: 1);

        var made = await shop.SendAsync(shop.Warehouse, shop.Locations[1], 2.5m, shop.Sugar);

        Assert.True(made.IsSuccess, made.Error);
        var line = Assert.Single(made.Value.Lines);
        Assert.Equal(("Azúcar estándar", 2.5m, (decimal?)30m, (decimal?)75m, 5m, 2.5m, 0m, 2.5m),
            (line.SupplyItemName, line.Quantity, line.UnitCost, line.TotalCost, line.FromQuantityBefore, line.FromQuantityAfter,
                line.ToQuantityBefore, line.ToQuantityAfter));
        Assert.Equal(75m, made.Value.TotalCost);
        Assert.NotEqual(made.Value.OutMovementNumber, made.Value.InMovementNumber);
        Assert.Equal([made.Value.Id], (await shop.ListAsync("anaquel")).Select(t => t.Id));
        Assert.Equal([made.Value.Id], (await shop.ListAsync(made.Value.TransferNumber[^4..])).Select(t => t.Id));
    }

    private sealed class Shop
    {
        private readonly SqlServerTestDatabase _database;

        private Shop(SqlServerTestDatabase database, Guid tenantId, List<Guid> locations, Guid soda, Guid sugar)
        {
            _database = database;
            TenantId = tenantId;
            Locations = locations;
            Soda = soda;
            Sugar = sugar;
        }

        public Guid TenantId { get; }
        public Guid Warehouse => Locations[0];
        /// <summary>The warehouse first, then the shelves.</summary>
        public List<Guid> Locations { get; }
        public Guid Soda { get; }
        public Guid Sugar { get; }

        /// <summary>A shop whose warehouse has 24 sodas at $12 and 5 kg of sugar at $30, and other empty locations.</summary>
        public static async Task<Shop> OpenAsync(SqlServerTestDatabase database, int extraLocations)
        {
            var slug = Guid.NewGuid().ToString("N")[..10];
            var tenant = new Tenant("Abarrotes", slug, BusinessType.Retail, $"{slug}@demo.com");
            var warehouse = new InventoryLocation(tenant.Id, "Bodega", "BOD", isDefault: true);
            var others = Enumerable.Range(1, extraLocations).Select(i => new InventoryLocation(tenant.Id, $"Anaquel {i}", $"AN{i}")).ToList();
            var soda = new SupplyItem(tenant.Id, "Refresco de cola 600 ml", UnitType.Unit, defaultCost: 12m);
            var sugar = new SupplyItem(tenant.Id, "Azúcar estándar", UnitType.Kg, defaultCost: 30m);

            await using (var context = database.CreateContext(tenant.Id))
            {
                context.Tenants.Add(tenant);
                context.TenantSettings.Add(new TenantSettings(tenant.Id, TenantCapability.Retail | TenantCapability.Inventory));
                context.InventoryLocations.Add(warehouse);
                context.InventoryLocations.AddRange(others);
                context.SupplyItems.AddRange(soda, sugar);
                await context.SaveChangesAsync();
            }

            var shop = new Shop(database, tenant.Id, [warehouse.Id, .. others.Select(o => o.Id)], soda.Id, sugar.Id);
            var (tenantService, user, clock) = shop.Request();
            await using (var context = shop.Context(tenantService, user, clock))
            {
                var posted = await new InventoryLedger(context, new FolioGenerator(context)).PostAsync(tenant.Id, new InventoryPosting(
                    InventoryMovementType.Entry, InventoryMovementReason.InitialLoad, Now.AddDays(-1),
                    [new InventoryPostingLine(soda.Id, warehouse.Id, 24m) { UnitCost = 12m }, new InventoryPostingLine(sugar.Id, warehouse.Id, 5m) { UnitCost = 30m }]),
                    CancellationToken.None);
                Assert.True(posted.IsSuccess, posted.Error);
                await context.SaveChangesAsync();
            }

            return shop;
        }

        public CreateInventoryTransferCommand Command(Guid from, Guid to, decimal quantity, Guid? supply = null) => new()
        {
            IdempotencyKey = Guid.NewGuid(),
            FromLocationId = from,
            ToLocationId = to,
            Lines = [new InventoryTransferLineRequest { SupplyItemId = supply ?? Soda, Quantity = quantity }]
        };

        public Task<Result<InventoryTransferDto>> SendAsync(Guid from, Guid to, decimal quantity, Guid? supply = null)
            => SendAsync(Command(from, to, quantity, supply));

        /// <summary>Through the transaction of the pipeline, with the services of production.</summary>
        public async Task<Result<InventoryTransferDto>> SendAsync(CreateInventoryTransferCommand command)
        {
            var (tenant, user, clock) = Request();
            await using var context = Context(tenant, user, clock);
            return await new TransactionBehavior<CreateInventoryTransferCommand, Result<InventoryTransferDto>>(context)
                .Handle(command, () => new CreateInventoryTransferCommandHandler(
                    context, tenant, user, clock, new FolioGenerator(context), new InventoryLocationLock(context),
                    new InventoryLedger(context, new FolioGenerator(context))).Handle(command, CancellationToken.None), CancellationToken.None);
        }

        public async Task<List<InventoryTransferListItemDto>> ListAsync(string search)
        {
            var (tenant, user, clock) = Request();
            await using var context = Context(tenant, user, clock);
            return (await new GetInventoryTransfersQueryHandler(context).Handle(new GetInventoryTransfersQuery { Search = search }, CancellationToken.None)).Items;
        }

        private (InMemoryApplicationDbContext.TestCurrentTenantService Tenant, Clerk User, Clock Clock) Request()
        {
            var tenant = new InMemoryApplicationDbContext.TestCurrentTenantService();
            tenant.SetTenant(TenantId);
            return (tenant, new Clerk(), new Clock());
        }

        private Persistence.ApplicationDbContext Context(ICurrentTenantService tenant, Clerk user, Clock clock)
            => _database.CreateProductionLikeContext(
                tenant,
                new InMemoryApplicationDbContext.NullMediator(),
                new AuditableEntityInterceptor(user, clock),
                new TenantSaveChangesInterceptor(tenant));
    }

    private sealed class Clerk : ICurrentUserService
    {
        public string? UserId => "9b1f3c0e-5d2a-4b7e-8c11-2f6a9d4e7b30";
        public string? UserName => "encargado";
        public string? Role => "TENANT_ADMIN";
        public string? TenantRole => "Owner";
        public IReadOnlyCollection<string> Permissions => [];
        public Guid? TenantId => null;
    }

    private sealed class Clock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => Now;
    }
}
