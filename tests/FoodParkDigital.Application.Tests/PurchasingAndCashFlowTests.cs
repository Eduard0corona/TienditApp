using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Finance.Commands;
using FoodParkDigital.Application.Finance;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Purchasing.Commands;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Finance.Entities;
using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

public class PurchasingAndCashFlowTests
{
    [Fact]
    public async Task ReceivePurchaseOrder_ShouldIncreaseStock_AndCreateInventoryMovement()
    {
        var tenantService = new TestCurrentTenantService();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new TestApplicationDbContext(options, tenantService, new NullMediator());
        var tenant = new Tenant("Tenant Compras", "tenant-compras", BusinessType.Retail, "tenant@test.com");
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();
        tenantService.SetTenant(tenant.Id);

        context.TenantSettings.Add(new TenantSettings(tenant.Id, TenantCapability.Retail | TenantCapability.Inventory));
        await context.SaveChangesAsync();

        var supplier = new Domain.Purchasing.Entities.Supplier(tenant.Id, "Proveedor 1");
        var category = new Category(tenant.Id, "General", null, 0);
        var supply = new Domain.Inventory.Entities.SupplyItem(tenant.Id, "Detergente", UnitType.Liter, defaultCost: 10m);
        var location = new Domain.Inventory.Entities.InventoryLocation(tenant.Id, "Almacen", "MAIN", isDefault: true);

        context.Suppliers.Add(supplier);
        context.Categories.Add(category);
        context.SupplyItems.Add(supply);
        context.InventoryLocations.Add(location);
        await context.SaveChangesAsync();

        var createHandler = new CreatePurchaseOrderCommandHandler(context, tenantService, new FolioGenerator(context));
        var createResult = await createHandler.Handle(new CreatePurchaseOrderCommand
        {
            SupplierId = supplier.Id,
            MarkAsSent = true,
            Lines =
            [
                new CreatePurchaseOrderLineRequest
                {
                    SupplyItemId = supply.Id,
                    InventoryLocationId = location.Id,
                    Quantity = 5,
                    UnitCost = 12,
                    TaxPercent = 0
                }
            ]
        }, CancellationToken.None);

        Assert.True(createResult.IsSuccess, createResult.Error);

        var createdOrder = await context.PurchaseOrders
            .Include(x => x.Lines)
            .FirstAsync(x => x.Id == createResult.Value.Id);

        var receiveHandler = new ReceivePurchaseOrderCommandHandler(context, tenantService, new FolioGenerator(context));
        var receiveResult = await receiveHandler.Handle(new ReceivePurchaseOrderCommand
        {
            PurchaseOrderId = createdOrder.Id,
            Lines =
            [
                new ReceivePurchaseOrderLineRequest
                {
                    PurchaseOrderLineId = createdOrder.Lines.First().Id,
                    QuantityReceived = 5
                }
            ]
        }, CancellationToken.None);

        Assert.True(receiveResult.IsSuccess, receiveResult.Error);
        Assert.Equal(Domain.Purchasing.Enums.PurchaseOrderStatus.Received, receiveResult.Value.Status);

        var stock = await context.InventoryStocks.SingleAsync();
        Assert.Equal(5m, stock.OnHandQuantity);

        var movement = await context.InventoryMovements.SingleAsync();
        Assert.Equal(Domain.Inventory.Enums.InventoryMovementReason.Purchase, movement.MovementReason);
        Assert.Equal(Domain.Inventory.Enums.InventoryMovementType.Entry, movement.MovementType);
    }

    [Fact]
    public async Task AddOrderPayment_WithOpenCashSession_ShouldRegisterCashTransaction()
    {
        var tenantService = new TestCurrentTenantService();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new TestApplicationDbContext(options, tenantService, new NullMediator());

        var tenant = new Tenant("Tenant Caja", "tenant-caja", BusinessType.Retail, "tenant@test.com");
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();
        tenantService.SetTenant(tenant.Id);

        context.TenantSettings.Add(new TenantSettings(tenant.Id, TenantCapability.Retail | TenantCapability.Inventory));
        await context.SaveChangesAsync();

        var category = new Category(tenant.Id, "General", null, 0);
        var product = new Product(tenant.Id, category.Id, "Producto", null, 100m, "MXN");
        context.Categories.Add(category);
        context.Products.Add(product);

        var order = new Order(
            tenant.Id,
            customerUserId: Guid.NewGuid(),
            temporaryCustomerId: null,
            createdByBusiness: true,
            orderNumber: "ORD-000001",
            businessType: BusinessType.Retail,
            orderType: OrderType.Retail,
            customerName: "Cliente Test",
            customerPhone: "+525500000000",
            customerNotes: null,
            tableNumber: null);
        order.AddItem(product.Id, product.Name, 1, product.PriceAmount);
        context.Orders.Add(order);

        var cashSession = new CashSession(tenant.Id, DateTimeOffset.UtcNow, "tester", 100m);
        context.CashSessions.Add(cashSession);
        await context.SaveChangesAsync();

        var handler = new AddOrderPaymentCommandHandler(
            context,
            new TestDateTimeProvider(),
            new OrderCashService(context, new CashierUser()));

        var result = await handler.Handle(new AddOrderPaymentCommand
        {
            OrderId = order.Id,
            Amount = 50m,
            Method = OrderPaymentMethod.Cash
        }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);

        var transaction = await context.CashTransactions.SingleAsync();
        Assert.Equal(CashTransactionType.SalePayment, transaction.TransactionType);
        Assert.Equal(50m, transaction.Amount);
        Assert.Equal(order.Id, transaction.OrderId);
    }

    private sealed class TestApplicationDbContext : ApplicationDbContext
    {
        public TestApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options,
            ICurrentTenantService tenantService,
            IMediator mediator)
            : base(options, tenantService, mediator)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Order>()
                .Property(o => o.Version)
                .IsRequired(false)
                .ValueGeneratedNever();
        }
    }

    private sealed class TestCurrentTenantService : ICurrentTenantService
    {
        public Guid TenantId { get; private set; } = Guid.NewGuid();
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    private sealed class TestDateTimeProvider : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 2, 17, 18, 0, 0, TimeSpan.Zero);
    }

    private sealed class NullMediator : IMediator
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
            => Task.FromResult<TResponse>(default!);

        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest
            => Task.CompletedTask;

        public Task Publish(object notification, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken ct = default)
            where TNotification : INotification
            => Task.CompletedTask;

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default)
            => AsyncEnumerable.Empty<TResponse>();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(object request, CancellationToken ct = default)
            => AsyncEnumerable.Empty<TResponse>();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default)
            => AsyncEnumerable.Empty<object?>();

        public Task<object?> Send(object request, CancellationToken ct = default)
            => Task.FromResult<object?>(null);
    }

    private sealed class CashierUser : ICurrentUserService
    {
        public string? UserId => null;
        public string? UserName => "tester";
        public string? Role => "TENANT_ADMIN";
        public string? TenantRole => null;
        public IReadOnlyCollection<string> Permissions => [];
        public Guid? TenantId => null;
    }
}
