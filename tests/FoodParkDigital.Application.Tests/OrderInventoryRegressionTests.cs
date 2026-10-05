using AutoMapper;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Mappings;
using FoodParkDigital.Application.Customers.Commands;
using FoodParkDigital.Application.Inventory.Commands;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Application.Purchasing.Commands;
using FoodParkDigital.Application.Reports.Queries;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Inventory.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
using FoodParkDigital.Application.Tests.Support;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// Regressions for the order, inventory, purchasing and reporting flows fixed after the audit.
/// </summary>
public class OrderInventoryRegressionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task KitchenBoard_ShouldListOrdersCreatedFromThePos()
    {
        var (context, tenant, _) = CreateContext();
        await using var _ = context;
        context.TenantSettings.Add(new TenantSettings(tenant.TenantId, TenantCapability.Food | TenantCapability.Kitchen));
        var pending = NewOrder(tenant.TenantId, BusinessType.Food, OrderType.Retail, "ORD-000001");
        var confirmed = NewOrder(tenant.TenantId, BusinessType.Food, OrderType.Retail, "ORD-000002");
        confirmed.TransitionTo(OrderStatus.Confirmed, new FoodOrderStateMachine(), "pos");
        var ready = NewOrder(tenant.TenantId, BusinessType.Food, OrderType.Retail, "ORD-000003");
        ready.TransitionTo(OrderStatus.Ready, new FoodOrderStateMachine(), "pos");
        context.Orders.AddRange(pending, confirmed, ready);
        await context.SaveChangesAsync();

        var result = await new GetKitchenOrdersQueryHandler(context, tenant, CreateMapper())
            .Handle(new GetKitchenOrdersQuery(), CancellationToken.None);

        Assert.Equal(
            new[] { confirmed.Id, pending.Id }.OrderBy(x => x),
            result.Select(x => x.Id).OrderBy(x => x));
    }

    [Fact]
    public async Task KitchenBoard_ShouldSkipServiceOrdersOfAFoodBusiness()
    {
        var (context, tenant, _) = CreateContext();
        await using var _ = context;
        context.TenantSettings.Add(new TenantSettings(
            tenant.TenantId,
            TenantCapability.Food | TenantCapability.Service | TenantCapability.Kitchen));
        var ticket = NewOrder(tenant.TenantId, BusinessType.Food, OrderType.Retail, "ORD-000001");
        var serviceOrder = NewOrder(tenant.TenantId, BusinessType.Food, OrderType.Service, "ORD-000002");
        Assert.Equal(OrderStatus.Pending, serviceOrder.Status);
        context.Orders.AddRange(ticket, serviceOrder);
        await context.SaveChangesAsync();

        var result = await new GetKitchenOrdersQueryHandler(context, tenant, CreateMapper())
            .Handle(new GetKitchenOrdersQuery(), CancellationToken.None);

        Assert.Equal([ticket.Id], result.Select(x => x.Id));
    }

    [Fact]
    public async Task CompletingPiece_ShouldConsumeItsRecipeInTheSameSave()
    {
        var (context, tenant, _) = CreateContext();
        await using var _ = context;
        var setup = await SeedServiceOrderAsync(context, tenant.TenantId, pieces: 1, stock: 5m);

        var result = await NewPieceHandler(context).Handle(new UpdateServiceOrderPieceStatusCommand
        {
            OrderId = setup.Order.Id,
            PieceId = setup.Order.ServicePieces.Single().Id,
            NewStatus = ServiceOrderPieceStatus.Completed
        }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(4m, (await context.InventoryStocks.AsNoTracking().SingleAsync()).OnHandQuantity);
        var movement = await context.InventoryMovements.AsNoTracking().SingleAsync();
        Assert.StartsWith($"ORDER:{setup.Order.Id:N}:PIECE:", movement.ExternalReference);
        var stored = await context.Orders.AsNoTracking().SingleAsync();
        Assert.Equal(OrderStatus.Completed, stored.Status);
    }

    [Fact]
    public async Task CompletingPiece_WithoutStock_ShouldLeaveThePieceInProgress()
    {
        var (context, tenant, _) = CreateContext();
        await using var _ = context;
        var setup = await SeedServiceOrderAsync(context, tenant.TenantId, pieces: 1, stock: 0m);
        var pieceId = setup.Order.ServicePieces.Single().Id;

        await Assert.ThrowsAsync<DomainException>(() => NewPieceHandler(context).Handle(new UpdateServiceOrderPieceStatusCommand
        {
            OrderId = setup.Order.Id,
            PieceId = pieceId,
            NewStatus = ServiceOrderPieceStatus.Completed
        }, CancellationToken.None));

        var storedPiece = await context.ServiceOrderPieces.AsNoTracking().SingleAsync(x => x.Id == pieceId);
        Assert.Equal(ServiceOrderPieceStatus.InProgress, storedPiece.Status);
        Assert.Empty(await context.InventoryMovements.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ConcurrentPieceCompletion_ShouldRetryAndCompleteTheOrder()
    {
        var (context, tenant, options) = CreateContext();
        await using var _ = context;
        var setup = await SeedServiceOrderAsync(context, tenant.TenantId, pieces: 2, stock: 10m);
        var pieces = setup.Order.ServicePieces.ToList();

        // While this request completes the second piece, another employee completes the first one and
        // saves first: this save hits the order's row version and must be retried with fresh data.
        context.BeforeNextSave = async () =>
        {
            await using var other = new TestDbContext(options, tenant);
            var otherResult = await NewPieceHandler(other).Handle(new UpdateServiceOrderPieceStatusCommand
            {
                OrderId = setup.Order.Id,
                PieceId = pieces[0].Id,
                NewStatus = ServiceOrderPieceStatus.Completed
            }, CancellationToken.None);
            Assert.True(otherResult.IsSuccess, otherResult.Error);
            throw new DbUpdateConcurrencyException("Simulated concurrent update of the order row.");
        };

        var result = await NewPieceHandler(context).Handle(new UpdateServiceOrderPieceStatusCommand
        {
            OrderId = setup.Order.Id,
            PieceId = pieces[1].Id,
            NewStatus = ServiceOrderPieceStatus.Completed
        }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        await using var verify = new TestDbContext(options, tenant);
        var stored = await verify.Orders.Include(x => x.ServicePieces).SingleAsync();
        Assert.All(stored.ServicePieces, piece => Assert.Equal(ServiceOrderPieceStatus.Completed, piece.Status));
        Assert.Equal(OrderStatus.Completed, stored.Status);
        Assert.Equal(8m, (await verify.InventoryStocks.SingleAsync()).OnHandQuantity);
    }

    [Fact]
    public async Task MixedServiceOrder_ShouldConsumeProductsSoldWithTheService()
    {
        var (context, tenant, _) = CreateContext();
        await using var _ = context;
        var setup = await SeedServiceOrderAsync(context, tenant.TenantId, pieces: 1, stock: 10m);
        var polishSupply = new SupplyItem(tenant.TenantId, "Grasa", UnitType.Unit);
        context.SupplyItems.Add(polishSupply);
        var polishStock = new InventoryStock(tenant.TenantId, polishSupply.Id, setup.Location.Id);
        polishStock.ApplyMovement(10m, 5m, Now);
        context.InventoryStocks.Add(polishStock);
        var polishProductId = Guid.NewGuid();
        context.ProductSupplyRequirements.Add(new ProductSupplyRequirement(tenant.TenantId, polishProductId, polishSupply.Id, 2m));
        var order = NewOrder(tenant.TenantId, BusinessType.Service, OrderType.Service, "ORD-000010");
        order.AddItem(setup.ServiceProductId, "Lavado", 1, 200m);
        order.AddItem(polishProductId, "Grasa para calzado", 1, 50m);
        order.AddServicePiece("Par", null, null, null, null, null, null, null, null, setup.ServiceProductId);
        order.TransitionTo(OrderStatus.Confirmed, new ServiceOrderStateMachine(), "pos");
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        await NewInventoryService(context).ApplyConsumptionForOrderAsync(order, CancellationToken.None);
        await context.SaveChangesAsync();

        Assert.Equal(8m, (await context.InventoryStocks.AsNoTracking().SingleAsync(x => x.SupplyItemId == polishSupply.Id)).OnHandQuantity);
        // The service recipe is consumed per completed piece, not when the order is confirmed.
        Assert.Equal(10m, (await context.InventoryStocks.AsNoTracking().SingleAsync(x => x.SupplyItemId == setup.Supply.Id)).OnHandQuantity);
    }

    [Fact]
    public async Task CancellingThroughStatusChange_ShouldCancelOpenPieces()
    {
        var (context, tenant, _) = CreateContext();
        await using var _ = context;
        var setup = await SeedServiceOrderAsync(context, tenant.TenantId, pieces: 2, stock: 10m);

        var result = await new UpdateOrderStatusCommandHandler(context, new TestCurrentUserService(), NoOpOrderInventoryService.Instance).Handle(
            new UpdateOrderStatusCommand { OrderId = setup.Order.Id, NewStatus = OrderStatus.Cancelled },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var pieces = await context.ServiceOrderPieces.AsNoTracking().ToListAsync();
        Assert.All(pieces, piece => Assert.Equal(ServiceOrderPieceStatus.Cancelled, piece.Status));
    }

    [Fact]
    public async Task CancellingAPaidOrder_ShouldBeRejected()
    {
        var (context, tenant, _) = CreateContext();
        await using var _ = context;
        var order = NewOrder(tenant.TenantId, BusinessType.Retail, OrderType.Retail, "ORD-000020");
        order.RegisterPayment(50m, OrderPaymentMethod.Cash, Now, null, null, null, null);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<DomainException>(() =>
            new UpdateOrderStatusCommandHandler(context, new TestCurrentUserService(), NoOpOrderInventoryService.Instance).Handle(
                new UpdateOrderStatusCommand { OrderId = order.Id, NewStatus = OrderStatus.Cancelled },
                CancellationToken.None));
        Assert.Equal(OrderStatus.Pending, (await context.Orders.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public void Reception_ShouldRejectRepeatedLines()
    {
        var lineId = Guid.NewGuid();
        var result = new ReceivePurchaseOrderCommandValidator().Validate(new ReceivePurchaseOrderCommand
        {
            PurchaseOrderId = Guid.NewGuid(),
            Lines =
            [
                new ReceivePurchaseOrderLineRequest { PurchaseOrderLineId = lineId, QuantityReceived = 5 },
                new ReceivePurchaseOrderLineRequest { PurchaseOrderLineId = lineId, QuantityReceived = 5 }
            ]
        });

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task TransferIn_ShouldNotChangeTheAverageCost()
    {
        var (context, tenant, _) = CreateContext();
        await using var _ = context;
        var supply = new SupplyItem(tenant.TenantId, "Harina", UnitType.Kg);
        supply.ApplyInboundCost(10m, 0m, 5m);
        var location = new InventoryLocation(tenant.TenantId, "Sucursal", "BR1");
        context.SupplyItems.Add(supply);
        context.InventoryLocations.Add(location);
        await context.SaveChangesAsync();

        var result = await NewMovementHandler(context, tenant).Handle(new CreateInventoryMovementCommand
        {
            MovementType = InventoryMovementType.TransferIn,
            MovementReason = InventoryMovementReason.Other,
            Lines =
            [
                new CreateInventoryMovementLineRequest { SupplyItemId = supply.Id, InventoryLocationId = location.Id, Quantity = 5 }
            ]
        }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(10m, (await context.SupplyItems.AsNoTracking().SingleAsync()).AverageCost);
        Assert.Equal(10m, result.Value!.Lines.Single().UnitCost);
    }

    [Theory]
    [InlineData("ORDER:abc:CONSUMPTION")]
    [InlineData(" po:123:RECEIPT")]
    public void ManualMovement_ShouldRejectReservedExternalReferences(string reference)
    {
        var result = new CreateInventoryMovementCommandValidator().Validate(new CreateInventoryMovementCommand
        {
            MovementType = InventoryMovementType.Entry,
            MovementReason = InventoryMovementReason.Purchase,
            ExternalReference = reference,
            Lines = [new CreateInventoryMovementLineRequest { SupplyItemId = Guid.NewGuid(), InventoryLocationId = Guid.NewGuid(), Quantity = 1 }]
        });

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ManualMovement_ShouldRejectADuplicatedExternalReference()
    {
        var (context, tenant, _) = CreateContext();
        await using var _ = context;
        var supply = new SupplyItem(tenant.TenantId, "Harina", UnitType.Kg);
        var location = new InventoryLocation(tenant.TenantId, "Almacen", "MAIN", isDefault: true);
        context.SupplyItems.Add(supply);
        context.InventoryLocations.Add(location);
        await context.SaveChangesAsync();
        var command = new CreateInventoryMovementCommand
        {
            MovementType = InventoryMovementType.Entry,
            MovementReason = InventoryMovementReason.Purchase,
            ExternalReference = "FAC-001",
            Lines = [new CreateInventoryMovementLineRequest { SupplyItemId = supply.Id, InventoryLocationId = location.Id, Quantity = 1, UnitCost = 10 }]
        };

        var first = await NewMovementHandler(context, tenant).Handle(command, CancellationToken.None);
        var second = await NewMovementHandler(context, tenant).Handle(command, CancellationToken.None);

        Assert.True(first.IsSuccess, first.Error);
        Assert.True(second.IsFailure);
        Assert.Equal(1m, (await context.InventoryStocks.AsNoTracking().SingleAsync()).OnHandQuantity);
    }

    [Fact]
    public async Task UpsertRecipe_ShouldReplaceTheRequirementWhenItsSupplyChanges()
    {
        var (context, tenant, _) = CreateContext();
        await using var _ = context;
        var product = new Product(tenant.TenantId, Guid.NewGuid(), "Hamburguesa", null, 100m, "MXN");
        var oldSupply = new SupplyItem(tenant.TenantId, "Pan blanco", UnitType.Unit);
        var newSupply = new SupplyItem(tenant.TenantId, "Pan integral", UnitType.Unit);
        var requirement = new ProductSupplyRequirement(tenant.TenantId, product.Id, oldSupply.Id, 1m);
        context.Products.Add(product);
        context.SupplyItems.AddRange(oldSupply, newSupply);
        context.ProductSupplyRequirements.Add(requirement);
        await context.SaveChangesAsync();

        var result = await new UpsertProductRecipeCommandHandler(context, tenant).Handle(new UpsertProductRecipeCommand
        {
            ProductId = product.Id,
            Lines = [new UpsertProductRecipeLineRequest { RequirementId = requirement.Id, SupplyItemId = newSupply.Id, Quantity = 2m }]
        }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var stored = await context.ProductSupplyRequirements.AsNoTracking().SingleAsync();
        Assert.Equal(newSupply.Id, stored.SupplyItemId);
        Assert.Equal(2m, stored.Quantity);
    }

    [Fact]
    public async Task DeleteCustomer_ShouldKeepCustomersWhoseOrdersStoreTheNormalizedPhone()
    {
        var (context, tenant, _) = CreateContext();
        await using var _ = context;
        context.TenantSettings.Add(new TenantSettings(tenant.TenantId, TenantCapability.Retail));
        var withOrders = new Customer(tenant.TenantId, "Ana", "55 1111 2222", null);
        var withoutOrders = new Customer(tenant.TenantId, "Luis", "55 3333 4444", null);
        context.Customers.AddRange(withOrders, withoutOrders);
        context.Orders.Add(NewOrder(tenant.TenantId, BusinessType.Retail, OrderType.Retail, "ORD-000030", "+525511112222"));
        await context.SaveChangesAsync();
        var handler = new DeleteCustomerCommandHandler(context, new PhoneNormalizationService());

        var blocked = await handler.Handle(new DeleteCustomerCommand(withOrders.Id), CancellationToken.None);
        var deleted = await handler.Handle(new DeleteCustomerCommand(withoutOrders.Id), CancellationToken.None);

        Assert.True(blocked.IsFailure);
        Assert.Contains("órdenes", blocked.Error);
        Assert.True(deleted.IsSuccess, deleted.Error);
        Assert.Equal(withOrders.Id, (await context.Customers.AsNoTracking().SingleAsync()).Id);
    }

    [Fact]
    public async Task OperationalReport_ShouldComputeProfitWithoutTaxAndOnlyForRecognizedOrders()
    {
        var (context, tenant, _) = CreateContext();
        await using var _ = context;
        var supplyId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        var delivered = NewOrder(tenant.TenantId, BusinessType.Retail, OrderType.Retail, "ORD-000041");
        delivered.TransitionTo(OrderStatus.Delivered, new RetailOrderStateMachine(), "pos");
        var cancelled = NewOrder(tenant.TenantId, BusinessType.Retail, OrderType.Retail, "ORD-000042");
        cancelled.Cancel(new RetailOrderStateMachine(), "pos");
        var open = NewOrder(tenant.TenantId, BusinessType.Retail, OrderType.Retail, "ORD-000043");
        open.TransitionTo(OrderStatus.Confirmed, new RetailOrderStateMachine(), "pos");
        foreach (var order in new[] { delivered, cancelled, open })
            order.CreatedAt = Now;
        context.Orders.AddRange(delivered, cancelled, open);
        context.InventoryMovements.AddRange(
            Movement(tenant.TenantId, delivered, InventoryMovementType.Exit, "CONSUMPTION", 3m, supplyId, locationId),
            Movement(tenant.TenantId, cancelled, InventoryMovementType.Exit, "CONSUMPTION", 2m, supplyId, locationId),
            Movement(tenant.TenantId, cancelled, InventoryMovementType.Entry, "REVERSAL", 2m, supplyId, locationId),
            Movement(tenant.TenantId, open, InventoryMovementType.Exit, "CONSUMPTION", 5m, supplyId, locationId));
        await context.SaveChangesAsync();

        var report = await new GetOperationalReportQueryHandler(context, new FixedClock(Now), tenant).Handle(new GetOperationalReportQuery
        {
            FromDate = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            ToDate = new DateTimeOffset(2026, 9, 30, 23, 59, 59, TimeSpan.Zero)
        }, CancellationToken.None);

        Assert.Equal(116m, report.TotalRevenue);
        Assert.Equal(16m, report.TotalTax);
        Assert.Equal(100m, report.NetRevenue);
        Assert.Equal(30m, report.TotalCogs);
        Assert.Equal(70m, report.GrossProfit);
        Assert.Equal(1, report.OrdersCompleted);
    }

    [Fact]
    public async Task OperationalReport_DefaultRange_ShouldBeTheMonthInTheTenantTimeZone()
    {
        var (context, tenant, _) = CreateContext();
        await using var _ = context;
        // Another tenant's settings come first: the report must use its own tenant's time zone.
        var otherTenantSettings = new TenantSettings(Guid.NewGuid());
        otherTenantSettings.Update(otherTenantSettings.Currency, "Asia/Tokyo", autoAcceptOrders: false, maxConcurrentOrders: 50);
        context.TenantSettings.Add(otherTenantSettings);
        context.TenantSettings.Add(new TenantSettings(tenant.TenantId)); // America/Mexico_City (UTC-6)
        await context.SaveChangesAsync();
        // 03:00 UTC on October 1st is still September 30th at 21:00 in Mexico City.
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 1, 3, 0, 0, TimeSpan.Zero));

        var report = await new GetOperationalReportQueryHandler(context, clock, tenant)
            .Handle(new GetOperationalReportQuery(), CancellationToken.None);

        Assert.Equal(new DateTimeOffset(2026, 9, 1, 6, 0, 0, TimeSpan.Zero), report.RangeFrom);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 6, 0, 0, TimeSpan.Zero).AddTicks(-1), report.RangeTo);
    }

    private static InventoryMovement Movement(
        Guid tenantId,
        Order order,
        InventoryMovementType type,
        string kind,
        decimal quantity,
        Guid supplyId,
        Guid locationId)
    {
        var movement = new InventoryMovement(
            tenantId,
            $"INV-{Guid.NewGuid():N}"[..20],
            type,
            type == InventoryMovementType.Exit ? InventoryMovementReason.Sale : InventoryMovementReason.Return,
            Now,
            referenceNumber: order.OrderNumber,
            externalReference: $"ORDER:{order.Id:N}:{kind}");
        movement.AddLine(tenantId, supplyId, locationId, quantity, unitCost: 10m);
        movement.RecalculateTotalAmount();
        return movement;
    }

    private static async Task<ServiceOrderSetup> SeedServiceOrderAsync(TestDbContext context, Guid tenantId, int pieces, decimal stock)
    {
        var supply = new SupplyItem(tenantId, "Shampoo", UnitType.Unit);
        supply.ApplyInboundCost(4m, 0m, 1m);
        var location = new InventoryLocation(tenantId, "Almacen", "MAIN", isDefault: true);
        var serviceProductId = Guid.NewGuid();
        context.SupplyItems.Add(supply);
        context.InventoryLocations.Add(location);
        context.ProductSupplyRequirements.Add(new ProductSupplyRequirement(tenantId, serviceProductId, supply.Id, 1m));
        if (stock > 0)
        {
            var inventory = new InventoryStock(tenantId, supply.Id, location.Id);
            inventory.ApplyMovement(stock, 4m, Now);
            context.InventoryStocks.Add(inventory);
        }

        var order = NewOrder(tenantId, BusinessType.Service, OrderType.Service, "ORD-000100");
        for (var index = 0; index < pieces; index++)
            order.AddServicePiece($"Par {index + 1}", null, null, null, null, null, null, null, null, serviceProductId);
        foreach (var piece in order.ServicePieces)
            order.ChangeServicePieceStatus(piece, ServiceOrderPieceStatus.InProgress, Now);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return new ServiceOrderSetup(order, supply, location, serviceProductId);
    }

    private static Order NewOrder(Guid tenantId, BusinessType businessType, OrderType orderType, string number, string phone = "+525500000000")
    {
        var order = new Order(
            tenantId: tenantId,
            customerUserId: Guid.NewGuid(),
            temporaryCustomerId: null,
            createdByBusiness: true,
            orderNumber: number,
            businessType: businessType,
            orderType: orderType,
            customerName: "Cliente",
            customerPhone: phone,
            customerNotes: null,
            tableNumber: null);
        order.AddItem(Guid.NewGuid(), "Producto", 1, 100m);
        return order;
    }

    private static UpdateServiceOrderPieceStatusCommandHandler NewPieceHandler(ApplicationDbContext context) =>
        new(context, new TestCurrentUserService(), NewInventoryService(context));

    private static OrderInventoryService NewInventoryService(ApplicationDbContext context) =>
        new(context, new FixedClock(Now), new FolioGenerator(context), NullLogger<OrderInventoryService>.Instance);

    private static CreateInventoryMovementCommandHandler NewMovementHandler(ApplicationDbContext context, ICurrentTenantService tenant) =>
        new(context, tenant, new TestCurrentUserService(), CreateMapper(), new FolioGenerator(context));

    private static IMapper CreateMapper() =>
        new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();

    private static (TestDbContext Context, TestCurrentTenantService Tenant, DbContextOptions<ApplicationDbContext> Options) CreateContext()
    {
        var tenant = new TestCurrentTenantService();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return (new TestDbContext(options, tenant), tenant, options);
    }

    private sealed record ServiceOrderSetup(Order Order, SupplyItem Supply, InventoryLocation Location, Guid ServiceProductId);

    private sealed class TestDbContext(DbContextOptions<ApplicationDbContext> options, ICurrentTenantService tenantService)
        : ApplicationDbContext(options, tenantService, new NullMediator())
    {
        /// <summary>Runs once, instead of the next save, to interleave a concurrent request.</summary>
        public Func<Task>? BeforeNextSave { get; set; }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (BeforeNextSave is { } interleave)
            {
                BeforeNextSave = null;
                await interleave();
            }

            return await base.SaveChangesAsync(cancellationToken);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // The in-memory provider does not generate SQL Server row versions.
            modelBuilder.Entity<Order>()
                .Property(o => o.Version)
                .IsRequired(false)
                .ValueGeneratedNever();
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class TestCurrentTenantService : ICurrentTenantService
    {
        public Guid TenantId { get; private set; } = Guid.NewGuid();
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public string? UserId { get; } = Guid.NewGuid().ToString();
        public string? UserName => "tester";
        public string? Role => "TENANT_ADMIN";
        public string? TenantRole => "TENANT_ADMIN";
        public IReadOnlyCollection<string> Permissions => [];
        public Guid? TenantId => null;
    }

    private sealed class NullMediator : IMediator
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default) => Task.FromResult<TResponse>(default!);
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => Task.CompletedTask;
        public Task Publish(object notification, CancellationToken ct = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken ct = default) where TNotification : INotification => Task.CompletedTask;
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => AsyncEnumerable.Empty<TResponse>();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(object request, CancellationToken ct = default) => AsyncEnumerable.Empty<TResponse>();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => AsyncEnumerable.Empty<object?>();
        public Task<object?> Send(object request, CancellationToken ct = default) => Task.FromResult<object?>(null);
    }
}
