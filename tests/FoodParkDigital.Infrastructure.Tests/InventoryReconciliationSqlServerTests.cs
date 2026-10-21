using FoodParkDigital.Application.Common.Behaviors;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Inventory;
using FoodParkDigital.Application.Inventory.Reconciliation;
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
/// Revisar que cuadre (Fase 27C, ADR 0039) in SQL Server set up as in production: the history adds up with the sign of the
/// ledger and four decimals, the same reconciliation sent at once records the gap once (the lock of the location), and
/// neither a transfer from the location nor a sale saving at the same time changes what is reconciled.
/// </summary>
public class InventoryReconciliationSqlServerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 18, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task Review_ShouldAddTheHistoryUp_WithTheSignOfTheLedger()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var shop = await Shop.OpenAsync(database);
        await shop.PostAsync(InventoryMovementType.Exit, shop.Sugar, shop.Warehouse, 0.3755m);
        await shop.PostAsync(InventoryMovementType.TransferOut, shop.Soda, shop.Warehouse, 4m);
        await shop.PostAsync(InventoryMovementType.TransferIn, shop.Soda, shop.Store, 4m);
        await shop.PostAsync(InventoryMovementType.ReturnToSupplier, shop.Soda, shop.Warehouse, 1m);
        Assert.Equal((3, 0), await shop.ReviewCountsAsync());

        await shop.SetStockAsync(shop.Sugar, shop.Warehouse, 4.6250m);

        var review = await shop.ReviewAsync();
        var sugar = Assert.Single(review.Mismatches.Items);
        Assert.Equal((3, "Azúcar estándar", 4.6250m, 4.6245m, 0.0005m, 2),
            (review.CheckedCount, sugar.SupplyItemName, sugar.OnHandQuantity, sugar.HistoryQuantity, sugar.Difference, sugar.MovementCount));
        var detail = await shop.DetailAsync(shop.Sugar, shop.Warehouse);
        Assert.Equal([(InventoryReconciliationEventKind.AfterLastMovement, 0.0005m, (decimal?)4.6245m, (decimal?)4.6250m)],
            detail.Events.Select(e => (e.Kind, e.Quantity, e.QuantityFrom, e.QuantityTo)));
    }

    [SqlServerFact]
    public async Task TheSameReconciliation_SentAtOnce_ShouldRecordTheGapOnce()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var shop = await Shop.OpenAsync(database);
        await shop.SetStockAsync(shop.Soda, shop.Warehouse, 26m);

        var sent = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => shop.ReconcileAsync(shop.Soda, shop.Warehouse, 2m)));

        Assert.All(sent, s => Assert.True(s.IsSuccess, s.Error));
        Assert.Equal([(0, 1), (0, 1), (1, 0)], sent.Select(s => (s.Value.ReconciledCount, s.Value.AlreadyAddedUpCount)).Order());
        await using var context = database.CreateContext(shop.TenantId);
        Assert.Equal(1, await context.InventoryMovements.CountAsync(m => m.MovementReason == InventoryMovementReason.LedgerReconciliation));
        Assert.Equal(26m, await context.InventoryStocks.Where(s => s.SupplyItemId == shop.Soda && s.InventoryLocationId == shop.Warehouse)
            .Select(s => s.OnHandQuantity).SingleAsync());
        Assert.Equal((2, 0), await shop.ReviewCountsAsync());
    }

    [SqlServerFact]
    public async Task ATransferAtTheSameTime_ShouldNotChangeWhatIsReconciled()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var shop = await Shop.OpenAsync(database);
        await shop.SetStockAsync(shop.Soda, shop.Warehouse, 26m);

        var reconcile = shop.ReconcileAsync(shop.Soda, shop.Warehouse, 2m);
        var transfer = shop.TransferAsync(shop.Warehouse, shop.Store, 5m);
        await Task.WhenAll(reconcile, transfer);

        Assert.True((await reconcile).IsSuccess, (await reconcile).Error);
        Assert.True((await transfer).IsSuccess, (await transfer).Error);
        await using var context = database.CreateContext(shop.TenantId);
        Assert.Equal([5m, 21m], (await context.InventoryStocks.Where(s => s.SupplyItemId == shop.Soda).Select(s => s.OnHandQuantity).ToListAsync()).Order());
        Assert.Equal((3, 0), await shop.ReviewCountsAsync());
    }

    [SqlServerFact]
    public async Task ASaleSavingWhenTheReconciliationStarts_ShouldBeInWhatIsReconciled()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync(readCommittedSnapshot: true);
        var shop = await Shop.OpenAsync(database);
        await shop.SetStockAsync(shop.Soda, shop.Warehouse, 26m);
        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var commit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // A sale does not take the lock of the location: it has saved its 3 sodas and not committed yet.
        var sale = shop.SellAsync(shop.Soda, shop.Warehouse, 3m, saved, commit.Task);
        await saved.Task;
        var reconcile = shop.ReconcileAsync(shop.Soda, shop.Warehouse, 2m);
        await Task.Delay(TimeSpan.FromSeconds(1));
        commit.SetResult();
        await Task.WhenAll(sale, reconcile);

        Assert.True((await sale).IsSuccess, (await sale).Error);
        Assert.True((await reconcile).IsSuccess, (await reconcile).Error);
        // The reconciliation waited for it and wrote the stock after it, so nothing else shows in the history.
        var detail = await shop.DetailAsync(shop.Soda, shop.Warehouse);
        Assert.Equal((23m, 23m, 0m), (detail.OnHandQuantity, detail.HistoryQuantity, detail.Difference));
        Assert.Equal(
            [
                (InventoryReconciliationEventKind.BetweenMovements, 2m, (decimal?)24m, (decimal?)26m),
                (InventoryReconciliationEventKind.Reconciled, -2m, 23m, 23m)
            ],
            detail.Events.Select(e => (e.Kind, e.Quantity, e.QuantityFrom, e.QuantityTo)));
    }

    private sealed class Shop
    {
        private readonly SqlServerTestDatabase _database;

        private Shop(SqlServerTestDatabase database, Guid tenantId, Guid warehouse, Guid store, Guid soda, Guid sugar)
        {
            _database = database;
            TenantId = tenantId;
            Warehouse = warehouse;
            Store = store;
            Soda = soda;
            Sugar = sugar;
        }

        public Guid TenantId { get; }
        public Guid Warehouse { get; }
        public Guid Store { get; }
        public Guid Soda { get; }
        public Guid Sugar { get; }

        /// <summary>A shop whose warehouse has 24 sodas at $12 and 5 kg of sugar at $30, and an empty store.</summary>
        public static async Task<Shop> OpenAsync(SqlServerTestDatabase database)
        {
            var slug = Guid.NewGuid().ToString("N")[..10];
            var tenant = new Tenant("Abarrotes", slug, BusinessType.Retail, $"{slug}@demo.com");
            var warehouse = new InventoryLocation(tenant.Id, "Bodega", "BOD", isDefault: true);
            var store = new InventoryLocation(tenant.Id, "Mostrador", "MOS");
            var soda = new SupplyItem(tenant.Id, "Refresco de cola 600 ml", UnitType.Unit, defaultCost: 12m);
            var sugar = new SupplyItem(tenant.Id, "Azúcar estándar", UnitType.Kg, defaultCost: 30m);

            await using (var context = database.CreateContext(tenant.Id))
            {
                context.Tenants.Add(tenant);
                context.TenantSettings.Add(new TenantSettings(tenant.Id, TenantCapability.Retail | TenantCapability.Inventory));
                context.InventoryLocations.AddRange(warehouse, store);
                context.SupplyItems.AddRange(soda, sugar);
                await context.SaveChangesAsync();
            }

            var shop = new Shop(database, tenant.Id, warehouse.Id, store.Id, soda.Id, sugar.Id);
            await shop.PostAsync(InventoryMovementType.Entry, soda.Id, warehouse.Id, 24m, 12m);
            await shop.PostAsync(InventoryMovementType.Entry, sugar.Id, warehouse.Id, 5m, 30m);
            return shop;
        }

        public async Task PostAsync(InventoryMovementType type, Guid supply, Guid location, decimal quantity, decimal? unitCost = null)
        {
            var (tenant, user, clock) = Request();
            await using var context = Context(tenant, user, clock);
            var posted = await new InventoryLedger(context, new FolioGenerator(context)).PostAsync(TenantId, new InventoryPosting(
                type, type == InventoryMovementType.Entry ? InventoryMovementReason.Purchase : InventoryMovementReason.ManualAdjustment, Now,
                [new InventoryPostingLine(supply, location, quantity) { UnitCost = unitCost }]), CancellationToken.None);
            Assert.True(posted.IsSuccess, posted.Error);
            await context.SaveChangesAsync();
        }

        /// <summary>
        /// A sale as the orders post it: in its transaction, with its folio and without the lock of the location. It
        /// tells <paramref name="saved"/> once its rows are written and commits when <paramref name="commit"/> ends.
        /// </summary>
        public async Task<Result<InventoryMovement>> SellAsync(Guid supply, Guid location, decimal quantity, TaskCompletionSource saved, Task commit)
        {
            var (tenant, user, clock) = Request();
            await using var context = Context(tenant, user, clock);
            return await new TransactionBehavior<Sale, Result<InventoryMovement>>(context).Handle(new Sale(), async () =>
            {
                var posted = await new InventoryLedger(context, new FolioGenerator(context)).PostAsync(TenantId, new InventoryPosting(
                    InventoryMovementType.Exit, InventoryMovementReason.Sale, Now, [new InventoryPostingLine(supply, location, quantity)]),
                    CancellationToken.None);
                if (posted.IsSuccess)
                    await context.SaveChangesAsync();
                saved.TrySetResult();
                await commit;
                return posted;
            }, CancellationToken.None);
        }

        /// <summary>What a script or an old version could leave: the stock changed without a movement.</summary>
        public async Task SetStockAsync(Guid supply, Guid location, decimal onHand)
        {
            await using var context = _database.CreateContext(TenantId);
            await context.InventoryStocks
                .Where(s => s.SupplyItemId == supply && s.InventoryLocationId == location)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.OnHandQuantity, onHand));
        }

        public async Task<InventoryReconciliationDto> ReviewAsync()
        {
            var (tenant, user, clock) = Request();
            await using var context = Context(tenant, user, clock);
            return await new GetInventoryReconciliationQueryHandler(context).Handle(new GetInventoryReconciliationQuery(), CancellationToken.None);
        }

        public async Task<(int Checked, int Mismatched)> ReviewCountsAsync()
        {
            var review = await ReviewAsync();
            return (review.CheckedCount, review.Mismatches.TotalCount);
        }

        public async Task<InventoryReconciliationDetailDto> DetailAsync(Guid supply, Guid location)
        {
            var (tenant, user, clock) = Request();
            await using var context = Context(tenant, user, clock);
            return await new GetInventoryReconciliationDetailQueryHandler(context)
                .Handle(new GetInventoryReconciliationDetailQuery(supply, location), CancellationToken.None);
        }

        /// <summary>Through the transaction of the pipeline, with the lock of production.</summary>
        public async Task<Result<InventoryReconciliationResultDto>> ReconcileAsync(Guid supply, Guid location, decimal expectedDifference)
        {
            var command = new ReconcileInventoryLedgerCommand
            {
                InventoryLocationId = location,
                Lines = [new InventoryReconciliationLineRequest { SupplyItemId = supply, ExpectedDifference = expectedDifference }]
            };
            var (tenant, user, clock) = Request();
            await using var context = Context(tenant, user, clock);
            return await new TransactionBehavior<ReconcileInventoryLedgerCommand, Result<InventoryReconciliationResultDto>>(context)
                .Handle(command, () => new ReconcileInventoryLedgerCommandHandler(
                    context, tenant, user, clock, new FolioGenerator(context), new InventoryLocationLock(context)).Handle(command, CancellationToken.None),
                    CancellationToken.None);
        }

        public async Task<Result<InventoryTransferDto>> TransferAsync(Guid from, Guid to, decimal quantity)
        {
            var command = new CreateInventoryTransferCommand
            {
                IdempotencyKey = Guid.NewGuid(),
                FromLocationId = from,
                ToLocationId = to,
                Lines = [new InventoryTransferLineRequest { SupplyItemId = Soda, Quantity = quantity }]
            };
            var (tenant, user, clock) = Request();
            await using var context = Context(tenant, user, clock);
            return await new TransactionBehavior<CreateInventoryTransferCommand, Result<InventoryTransferDto>>(context)
                .Handle(command, () => new CreateInventoryTransferCommandHandler(
                    context, tenant, user, clock, new FolioGenerator(context), new InventoryLocationLock(context),
                    new InventoryLedger(context, new FolioGenerator(context))).Handle(command, CancellationToken.None), CancellationToken.None);
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

    private sealed record Sale : ITransactionalRequest;

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
