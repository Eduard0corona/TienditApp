using FoodParkDigital.Application.Common.Behaviors;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Inventory;
using FoodParkDigital.Application.Inventory.Counts;
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
/// Conteo físico (Fase 27A, ADR 0037) in SQL Server set up as in production: the location's count lock keeps two starts
/// from opening two counts, the same post sent twice from moving the stock twice, and a capture from slipping into a
/// count being posted; the folios of counts started at once never repeat, and the items of a count are read by the base.
/// </summary>
public class InventoryCountSqlServerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 18, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task Starts_OfTheSameLocation_AtTheSameTime_ShouldOpenOneCount()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var shop = await Shop.OpenAsync(database);

        var starts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            shop.SendAsync(new StartInventoryCountCommand { InventoryLocationId = shop.Warehouse })));

        var started = Assert.Single(starts, s => s.IsSuccess);
        Assert.Equal("CNT-20261021-0001", started.Value.CountNumber);
        Assert.All(starts.Where(s => s.IsFailure), s => Assert.StartsWith("Ya hay un conteo abierto de Bodega", s.Error));
        await using var context = database.CreateContext(shop.TenantId);
        Assert.Equal(1, await context.InventoryCounts.CountAsync());
        Assert.Equal(2, await context.InventoryCountLines.CountAsync());
    }

    [SqlServerFact]
    public async Task Counts_OfDifferentLocations_StartedAtOnce_ShouldNeverRepeatAFolio()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var shop = await Shop.OpenAsync(database, extraLocations: 3);

        var starts = await Task.WhenAll(shop.Locations.Select(location =>
            shop.SendAsync(new StartInventoryCountCommand { InventoryLocationId = location, SupplyItemIds = [shop.Soda] })));

        Assert.All(starts, s => Assert.True(s.IsSuccess, s.Error));
        Assert.Equal(
            ["CNT-20261021-0001", "CNT-20261021-0002", "CNT-20261021-0003", "CNT-20261021-0004"],
            starts.Select(s => s.Value.CountNumber).Order());
    }

    [SqlServerFact]
    public async Task TheSamePost_SentTwiceAtOnce_ShouldMoveTheStockOnce()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var shop = await Shop.OpenAsync(database);
        var count = (await shop.SendAsync(new StartInventoryCountCommand { InventoryLocationId = shop.Warehouse })).Value;
        var lines = await shop.LinesAsync(count.Id);
        var recorded = await shop.SendAsync(new RecordInventoryCountCommand
        {
            CountId = count.Id,
            Lines = lines.Select(l => new InventoryCountEntry { LineId = l.Id, CountedQuantity = l.SupplyItemId == shop.Soda ? 20m : 3.25m }).ToList()
        });

        var posts = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            shop.SendAsync(new PostInventoryCountCommand { CountId = count.Id, ExpectedRevision = recorded.Value.Count.Revision })));

        Assert.Single(posts, p => p.IsSuccess);
        Assert.Equal(InventoryCount.NotOpenMessage, Assert.Single(posts, p => p.IsFailure).Error);
        await using var context = database.CreateContext(shop.TenantId);
        Assert.Equal([3.25m, 20m], (await context.InventoryStocks.Select(s => s.OnHandQuantity).ToListAsync()).Order());
        // Both items were short: one adjustment, once.
        Assert.Equal(1, await context.InventoryMovements.CountAsync(m => m.MovementReason == InventoryMovementReason.InventoryCount));
    }

    [SqlServerFact]
    public async Task ACapture_AndThePost_AtTheSameTime_ShouldNeverPostWhatWasNotReviewed()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var shop = await Shop.OpenAsync(database);
        var count = (await shop.SendAsync(new StartInventoryCountCommand { InventoryLocationId = shop.Warehouse })).Value;
        var lines = (await shop.LinesAsync(count.Id)).ToDictionary(l => l.SupplyItemId, l => l.Id);
        var reviewed = await shop.SendAsync(new RecordInventoryCountCommand
        {
            CountId = count.Id,
            Lines = [new InventoryCountEntry { LineId = lines[shop.Soda], CountedQuantity = 20m }]
        });

        var post = shop.SendAsync(new PostInventoryCountCommand { CountId = count.Id, ExpectedRevision = reviewed.Value.Count.Revision });
        var capture = shop.SendAsync(new RecordInventoryCountCommand
        {
            CountId = count.Id,
            Lines = [new InventoryCountEntry { LineId = lines[shop.Sugar], CountedQuantity = 1m }]
        });
        await Task.WhenAll(post, capture);

        // One of them goes first: the post applies only what was reviewed, or the capture changes the count and the post is refused.
        await using var context = database.CreateContext(shop.TenantId);
        var sugar = await context.InventoryStocks.SingleAsync(s => s.SupplyItemId == shop.Sugar);
        if (post.Result.IsSuccess)
        {
            Assert.Equal(InventoryCount.NotOpenMessage, capture.Result.Error);
            Assert.Equal(5m, sugar.OnHandQuantity);
            Assert.Null((await context.InventoryCountLines.SingleAsync(l => l.Id == lines[shop.Sugar])).CountedQuantity);
        }
        else
        {
            Assert.True(capture.Result.IsSuccess, capture.Result.Error);
            Assert.Equal(PostInventoryCountCommandHandler.ChangedMessage, post.Result.Error);
            Assert.Equal(InventoryCountStatus.InProgress, (await context.InventoryCounts.SingleAsync()).Status);
            Assert.Equal(5m, sugar.OnHandQuantity);
        }
    }

    [SqlServerFact]
    public async Task TheItemsOfACount_ShouldBeReadByTheDatabase_WithTheirDifferencesAndWhatTheyAreWorth()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var shop = await Shop.OpenAsync(database);
        var count = (await shop.SendAsync(new StartInventoryCountCommand { InventoryLocationId = shop.Warehouse })).Value;
        var lines = (await shop.LinesAsync(count.Id)).ToDictionary(l => l.SupplyItemId, l => l.Id);
        var recorded = await shop.SendAsync(new RecordInventoryCountCommand
        {
            CountId = count.Id,
            Lines = [new InventoryCountEntry { LineId = lines[shop.Soda], CountedQuantity = 22m, Notes = "Dos abolladas" }]
        });

        var differences = await shop.PageAsync(count.Id, withDifference: true);
        var pending = await shop.LinesAsync(count.Id, pending: true);
        var searched = await shop.LinesAsync(count.Id, search: "750105");
        var detail = await shop.CountAsync(count.Id);

        var soda = Assert.Single(differences.Items);
        Assert.Equal(((decimal?)22m, (decimal?)24m, (decimal?)-2m, (decimal?)24m, (decimal?)22m, (decimal?)-24m, "Dos abolladas"),
            (soda.CountedQuantity, soda.SystemQuantity, soda.Difference, soda.CurrentQuantity, soda.ResultingQuantity, soda.DifferenceValue, soda.Notes));
        Assert.Equal(recorded.Value.Count.Revision, differences.Revision);
        // Counting blind: what is not counted yet carries nothing of the system.
        var sugar = Assert.Single(pending);
        Assert.Equal(("Azúcar estándar", (decimal?)null, (decimal?)null), (sugar.SupplyItemName, sugar.CurrentQuantity, sugar.ResultingQuantity));
        Assert.Equal("Refresco de cola 600 ml", Assert.Single(searched).SupplyItemName);
        Assert.Equal((1, (decimal?)0m, (decimal?)24m), (detail.DifferenceLineCount, detail.SurplusValue, detail.ShortageValue));

        // Posted, each item shows what its adjustment did, and the count what the adjustments were worth.
        var posted = await shop.SendAsync(new PostInventoryCountCommand { CountId = count.Id, ExpectedRevision = detail.Revision });
        Assert.True(posted.IsSuccess, posted.Error);
        var history = (await shop.LinesAsync(count.Id)).ToDictionary(l => l.SupplyItemId);
        Assert.Equal(((decimal?)24m, (decimal?)22m, (decimal?)-24m), (history[shop.Soda].CurrentQuantity, history[shop.Soda].ResultingQuantity, history[shop.Soda].DifferenceValue));
        Assert.Equal(((decimal?)null, (decimal?)null), (history[shop.Sugar].CountedQuantity, history[shop.Sugar].CurrentQuantity));
        Assert.Equal((InventoryCountStatus.Posted, (decimal?)0m, (decimal?)24m, posted.Value.DecreaseMovementNumber),
            (posted.Value.Status, posted.Value.SurplusValue, posted.Value.ShortageValue, (await shop.CountAsync(count.Id)).DecreaseMovementNumber));

        // The list finds it by its location or its folio.
        Assert.Equal([count.Id], (await shop.ListAsync("bode")).Select(c => c.Id));
        Assert.Equal([count.Id], (await shop.ListAsync(count.CountNumber[^4..])).Select(c => c.Id));
        Assert.Empty(await shop.ListAsync("anaquel"));
    }

    private sealed class Shop
    {
        private readonly SqlServerTestDatabase _database;

        private Shop(SqlServerTestDatabase database, Guid tenantId, Guid warehouse, List<Guid> locations, Guid soda, Guid sugar)
        {
            _database = database;
            TenantId = tenantId;
            Warehouse = warehouse;
            Locations = locations;
            Soda = soda;
            Sugar = sugar;
        }

        public Guid TenantId { get; }
        public Guid Warehouse { get; }
        public List<Guid> Locations { get; }
        public Guid Soda { get; }
        public Guid Sugar { get; }

        /// <summary>A shop whose warehouse has 24 sodas at $12 and 5 kg of sugar at $30, and other empty locations.</summary>
        public static async Task<Shop> OpenAsync(SqlServerTestDatabase database, int extraLocations = 0)
        {
            var slug = Guid.NewGuid().ToString("N")[..10];
            var tenant = new Tenant("Abarrotes", slug, BusinessType.Retail, $"{slug}@demo.com");
            var warehouse = new InventoryLocation(tenant.Id, "Bodega", "BOD", isDefault: true);
            var others = Enumerable.Range(1, extraLocations).Select(i => new InventoryLocation(tenant.Id, $"Anaquel {i}", $"AN{i}")).ToList();
            var soda = new SupplyItem(tenant.Id, "Refresco de cola 600 ml", UnitType.Unit, barcode: "7501055300", defaultCost: 12m);
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

            var shop = new Shop(database, tenant.Id, warehouse.Id, [warehouse.Id, .. others.Select(o => o.Id)], soda.Id, sugar.Id);
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

        public Task<Result<InventoryCountDto>> SendAsync(StartInventoryCountCommand command)
            => InTransactionAsync(command, (context, tenant, user, clock) => new StartInventoryCountCommandHandler(
                context, tenant, user, clock, new FolioGenerator(context), new InventoryCountLock(context)).Handle(command, CancellationToken.None));

        public Task<Result<RecordedInventoryCountDto>> SendAsync(RecordInventoryCountCommand command)
            => InTransactionAsync(command, (context, tenant, user, clock) => new RecordInventoryCountCommandHandler(
                context, tenant, user, clock, new InventoryCountLock(context)).Handle(command, CancellationToken.None));

        public Task<Result<InventoryCountDto>> SendAsync(PostInventoryCountCommand command)
            => InTransactionAsync(command, (context, tenant, user, clock) => new PostInventoryCountCommandHandler(
                context, tenant, user, clock, new InventoryCountLock(context), new InventoryLedger(context, new FolioGenerator(context)))
                .Handle(command, CancellationToken.None));

        public async Task<List<InventoryCountLineDto>> LinesAsync(Guid countId, string? search = null, bool? pending = null, bool? withDifference = null)
            => (await PageAsync(countId, search, pending, withDifference)).Items;

        public async Task<InventoryCountLinesPage> PageAsync(Guid countId, string? search = null, bool? pending = null, bool? withDifference = null)
        {
            var (tenant, user, clock) = Request();
            await using var context = Context(tenant, user, clock);
            return await new GetInventoryCountLinesQueryHandler(context).Handle(
                new GetInventoryCountLinesQuery { CountId = countId, Search = search, Pending = pending, WithDifference = withDifference }, CancellationToken.None);
        }

        public async Task<List<InventoryCountListItemDto>> ListAsync(string search)
        {
            var (tenant, user, clock) = Request();
            await using var context = Context(tenant, user, clock);
            return (await new GetInventoryCountsQueryHandler(context).Handle(new GetInventoryCountsQuery { Search = search }, CancellationToken.None)).Items;
        }

        public async Task<InventoryCountDto> CountAsync(Guid countId)
        {
            var (tenant, user, clock) = Request();
            await using var context = Context(tenant, user, clock);
            return await new GetInventoryCountQueryHandler(context).Handle(new GetInventoryCountQuery(countId), CancellationToken.None);
        }

        /// <summary>Runs a command of the count through the transaction of the pipeline, with the services of production.</summary>
        private async Task<Result<T>> InTransactionAsync<TCommand, T>(
            TCommand command,
            Func<Persistence.ApplicationDbContext, ICurrentTenantService, Counter, Clock, Task<Result<T>>> handle)
            where TCommand : ITransactionalRequest
        {
            var (tenant, user, clock) = Request();
            await using var context = Context(tenant, user, clock);
            return await new TransactionBehavior<TCommand, Result<T>>(context)
                .Handle(command, () => handle(context, tenant, user, clock), CancellationToken.None);
        }

        private (InMemoryApplicationDbContext.TestCurrentTenantService Tenant, Counter User, Clock Clock) Request()
        {
            var tenant = new InMemoryApplicationDbContext.TestCurrentTenantService();
            tenant.SetTenant(TenantId);
            return (tenant, new Counter(), new Clock());
        }

        private Persistence.ApplicationDbContext Context(ICurrentTenantService tenant, Counter user, Clock clock)
            => _database.CreateProductionLikeContext(
                tenant,
                new InMemoryApplicationDbContext.NullMediator(),
                new AuditableEntityInterceptor(user, clock),
                new TenantSaveChangesInterceptor(tenant));
    }

    private sealed class Counter : ICurrentUserService
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
