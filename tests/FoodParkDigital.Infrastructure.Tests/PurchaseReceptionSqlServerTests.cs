using FoodParkDigital.Application.Common.Behaviors;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Purchasing.Commands;
using FoodParkDigital.Application.Purchasing.DTOs;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Inventory.Enums;
using FoodParkDigital.Domain.Purchasing.Entities;
using FoodParkDigital.Domain.Purchasing.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Persistence.Interceptors;
using FoodParkDigital.Infrastructure.Services;
using FoodParkDigital.Infrastructure.Tests.Support;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// A reception sent again with its key (a double click, a retry after a timeout) is received once, in SQL Server set up
/// as in production (retries, MARS) and through the transaction of the pipeline: the second copy answers with the
/// purchase order as it is, and when both arrive at once the late one is undone whole.
/// </summary>
public class PurchaseReceptionSqlServerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 17, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task SameReceptionTwice_ShouldReceiveOnce()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var store = await Store.OpenAsync(database);
        var key = Guid.NewGuid();

        var first = await store.ReceiveAsync(store.Reception(4m, key));
        var again = await store.ReceiveAsync(store.Reception(4m, key));
        var next = await store.ReceiveAsync(store.Reception(3m, Guid.NewGuid()));

        Assert.True(first.IsSuccess, first.Error);
        Assert.True(again.IsSuccess, again.Error);
        Assert.Equal(4m, Assert.Single(again.Value!.Lines).ReceivedQuantity);
        Assert.True(next.IsSuccess, next.Error);
        Assert.Equal(7m, Assert.Single(next.Value!.Lines).ReceivedQuantity);
        Assert.Equal(7m, await store.OnHandAsync());
        Assert.Equal(2, await store.ReceptionMovementsAsync());
    }

    [SqlServerFact]
    public async Task SameReceptionTwiceAtOnce_ShouldUndoTheLateCopyWhole()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var key = Guid.NewGuid();
        var storedFirst = new StoreConcurrentReception(database, key);
        var store = await Store.OpenAsync(database, storedFirst);

        var late = await store.ReceiveAsync(store.Reception(4m, key));

        // The other copy stored the reception first: this one answers with the purchase order, and nothing of it
        // remains (its stock, its change to the order or its folio).
        Assert.True(storedFirst.Fired);
        Assert.True(late.IsSuccess, late.Error);
        Assert.Equal(1, await store.ReceptionMovementsAsync());
        Assert.Equal(0m, await store.OnHandAsync());
        Assert.Equal(0m, Assert.Single(late.Value!.Lines).ReceivedQuantity);
        await using var check = database.CreateContext(store.TenantId);
        Assert.Empty(await check.FolioSequences.Where(s => s.TenantId == store.TenantId && s.Scope.StartsWith("INV-")).ToListAsync());
    }

    /// <summary>A business with a purchase order of 10 units of a supply, sent to its supplier.</summary>
    private sealed class Store
    {
        private readonly SqlServerTestDatabase _database;
        private readonly IInterceptor[] _interceptors;

        private Store(SqlServerTestDatabase database, Guid tenantId, Guid purchaseOrderId, Guid lineId, Guid supplyId, IInterceptor[] interceptors)
        {
            _database = database;
            TenantId = tenantId;
            PurchaseOrderId = purchaseOrderId;
            LineId = lineId;
            SupplyId = supplyId;
            _interceptors = interceptors;
        }

        public Guid TenantId { get; }
        public Guid PurchaseOrderId { get; }
        public Guid LineId { get; }
        public Guid SupplyId { get; }

        public static async Task<Store> OpenAsync(SqlServerTestDatabase database, params IInterceptor[] interceptors)
        {
            var slug = Guid.NewGuid().ToString("N")[..10];
            var tenant = new Tenant("Tienda", slug, BusinessType.Retail, $"{slug}@demo.com");
            var supplier = new Supplier(tenant.Id, "Proveedor");
            var supply = new SupplyItem(tenant.Id, "Harina", UnitType.Kg);
            var location = new InventoryLocation(tenant.Id, "Bodega", $"MAIN-{slug}", isDefault: true);
            var order = new PurchaseOrder(tenant.Id, supplier.Id, "OC-000001", Now.AddDays(-1));
            var line = order.AddLine(supply.Id, location.Id, 10m, 12m, 0m);
            order.MarkAsSent();

            await using var context = database.CreateContext(tenant.Id);
            context.Tenants.Add(tenant);
            context.TenantSettings.Add(new TenantSettings(tenant.Id, TenantCapability.Retail | TenantCapability.Inventory));
            context.Suppliers.Add(supplier);
            context.SupplyItems.Add(supply);
            context.InventoryLocations.Add(location);
            context.PurchaseOrders.Add(order);
            await context.SaveChangesAsync();

            return new Store(database, tenant.Id, order.Id, line.Id, supply.Id, interceptors);
        }

        public ReceivePurchaseOrderCommand Reception(decimal quantity, Guid key) => new()
        {
            PurchaseOrderId = PurchaseOrderId,
            ReceivedAt = Now,
            IdempotencyKey = key,
            Lines = [new ReceivePurchaseOrderLineRequest { PurchaseOrderLineId = LineId, QuantityReceived = quantity }]
        };

        /// <summary>Sends the reception through the transaction of the pipeline, with the services of production.</summary>
        public async Task<Result<PurchaseOrderDto>> ReceiveAsync(ReceivePurchaseOrderCommand command)
        {
            var tenant = new InMemoryApplicationDbContext.TestCurrentTenantService();
            tenant.SetTenant(TenantId);
            await using var context = _database.CreateProductionLikeContext(
                tenant,
                new NoMediator(),
                [.. _interceptors, new TenantSaveChangesInterceptor(tenant)]);
            var handler = new ReceivePurchaseOrderCommandHandler(context, tenant, new FolioGenerator(context));

            return await new TransactionBehavior<ReceivePurchaseOrderCommand, Result<PurchaseOrderDto>>(context)
                .Handle(command, () => handler.Handle(command, CancellationToken.None), CancellationToken.None);
        }

        public async Task<decimal> OnHandAsync()
        {
            await using var context = _database.CreateContext(TenantId);
            return await context.InventoryStocks.Where(s => s.SupplyItemId == SupplyId).SumAsync(s => s.OnHandQuantity);
        }

        public async Task<int> ReceptionMovementsAsync()
        {
            await using var context = _database.CreateContext(TenantId);
            return await context.InventoryMovements.CountAsync(m => m.MovementReason == InventoryMovementReason.Purchase);
        }
    }

    /// <summary>
    /// Plays the other copy of a reception sent twice: right before the late copy is saved, the first one (its movement,
    /// with the same key) is stored and committed from another connection.
    /// </summary>
    private sealed class StoreConcurrentReception(SqlServerTestDatabase database, Guid key) : SaveChangesInterceptor
    {
        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var lateCopy = eventData.Context!.ChangeTracker.Entries<InventoryMovement>()
                .FirstOrDefault(e => e.State == EntityState.Added && e.Entity.ExternalReference?.EndsWith($":RECV:{key:N}") == true);
            if (Fired || lateCopy is null)
                return result;

            Fired = true;
            var movement = lateCopy.Entity;
            var first = new InventoryMovement(
                movement.TenantId,
                "INV-OTRA-0001",
                InventoryMovementType.Entry,
                InventoryMovementReason.Purchase,
                movement.MovementDate,
                movement.ReferenceNumber,
                movement.ExternalReference);
            await using var other = database.CreateContext(movement.TenantId);
            other.InventoryMovements.Add(first);
            await other.SaveChangesAsync(cancellationToken);
            return result;
        }
    }

    private sealed class NoMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken ct = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken ct = default) where TNotification : INotification
            => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
