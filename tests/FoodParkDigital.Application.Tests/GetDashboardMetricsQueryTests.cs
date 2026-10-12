using System.Text.RegularExpressions;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Finance;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Application.Reports.Queries;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace FoodParkDigital.Application.Tests;

public class GetDashboardMetricsQueryTests
{
    private static readonly DateTimeOffset OrderDate = new(2026, 2, 17, 18, 0, 0, TimeSpan.Zero);

    private readonly ITestOutputHelper _output;

    public GetDashboardMetricsQueryTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task TopSellingItems_ShouldOnlyIncludeItemsFromCurrentTenant()
    {
        await using var fixture = TestFixture.Create();
        var tenantA = await fixture.AddTenantAsync("tenant-a", TenantCapability.Retail);
        var tenantB = await fixture.AddTenantAsync("tenant-b", TenantCapability.Retail);
        var productA = await fixture.AddProductAsync(tenantA, "Producto A", UnitType.Unit, 100m);
        var productB = await fixture.AddProductAsync(tenantB, "Producto B", UnitType.Unit, 10m);
        var orderA = await fixture.AddCompletedOrderAsync(tenantA, OrderType.Retail, productA, quantity: 2);
        // Tenant B outsells tenant A, so a leaked row would rank first in tenant A's top 10.
        await fixture.AddCompletedOrderAsync(tenantB, OrderType.Retail, productB, quantity: 50);

        var metricsA = await fixture.GetMetricsAsync(tenantA);

        Assert.DoesNotContain(metricsA.TopSellingItems, x => x.ProductId == productB.Id);
        var itemA = Assert.Single(metricsA.TopSellingItems);
        Assert.Equal(productA.Id, itemA.ProductId);
        Assert.Equal("Producto A", itemA.ProductName);
        Assert.Equal(2, itemA.QuantitySold);
        Assert.Equal(200m, itemA.TotalAmount);
        Assert.Equal(1, metricsA.CompletedOrders);
        Assert.Equal(orderA.Total, metricsA.TotalRevenue);

        var metricsB = await fixture.GetMetricsAsync(tenantB);

        Assert.DoesNotContain(metricsB.TopSellingItems, x => x.ProductId == productA.Id);
        var itemB = Assert.Single(metricsB.TopSellingItems);
        Assert.Equal(productB.Id, itemB.ProductId);
        Assert.Equal(50, itemB.QuantitySold);
        Assert.Equal(500m, itemB.TotalAmount);
    }

    [Fact]
    public async Task Revenue_ShouldCountThePaymentsOfTheRange_WhateverTheOrderStatus()
    {
        await using var fixture = TestFixture.Create();
        var tenant = await fixture.AddTenantAsync("tenant-a", TenantCapability.Retail);
        var product = await fixture.AddProductAsync(tenant, "Producto", UnitType.Unit, 100m);
        // Paid in full and delivered: 116.
        await fixture.AddCompletedOrderAsync(tenant, OrderType.Retail, product, quantity: 1);
        // Still open: 50 paid in the range, 20 the day before, 162 still owed.
        var open = new Order(tenant.Id, null, Guid.NewGuid(), true, "ORD-900001", tenant.BusinessType, OrderType.Retail,
            "Cliente", "5511111111", null, null);
        open.AddItem(product.Id, product.Name, 2, product.PriceAmount);
        open.RegisterPayment(20m, OrderPaymentMethod.Cash, OrderDate.AddDays(-2), null, null, null, null);
        open.RegisterPayment(50m, OrderPaymentMethod.Card, OrderDate, null, null, null, null);
        open.CreatedAt = OrderDate;
        fixture.Context.Orders.Add(open);
        await fixture.Context.SaveChangesAsync();

        var metrics = await fixture.GetMetricsAsync(tenant);

        Assert.Equal(166m, metrics.TotalRevenue);
        Assert.Equal(2, metrics.PaidOrders);
        Assert.Equal(83m, metrics.AverageOrderValue);
        Assert.Equal(1, metrics.UnpaidOrders);
        Assert.Equal(232m - 70m, metrics.OutstandingBalance);
    }

    [Fact]
    public async Task TopSellingItems_ShouldKeepInactiveAndDeletedProducts()
    {
        await using var fixture = TestFixture.Create();
        var tenant = await fixture.AddTenantAsync("tenant-a", TenantCapability.Retail | TenantCapability.Service);
        var inactiveService = await fixture.AddProductAsync(tenant, "Servicio inactivo", UnitType.Service, 150m);
        var deletedProduct = await fixture.AddProductAsync(tenant, "Producto borrado", UnitType.Unit, 40m);
        await fixture.AddCompletedOrderAsync(tenant, OrderType.Service, inactiveService, quantity: 3);
        await fixture.AddCompletedOrderAsync(tenant, OrderType.Retail, deletedProduct, quantity: 1);

        inactiveService.SetAvailability(false);
        fixture.Context.Products.Remove(deletedProduct);
        await fixture.Context.SaveChangesAsync();

        var metrics = await fixture.GetMetricsAsync(tenant);

        var service = Assert.Single(metrics.TopSellingItems, x => x.ProductId == inactiveService.Id);
        Assert.Equal("Service", service.ItemType);
        Assert.Equal(3, service.QuantitySold);

        var deleted = Assert.Single(metrics.TopSellingItems, x => x.ProductId == deletedProduct.Id);
        Assert.Equal("Producto borrado", deleted.ProductName);
        Assert.Equal("Product", deleted.ItemType);
        Assert.Equal(1, deleted.QuantitySold);
    }

    [Fact]
    public async Task TopSellingItems_ShouldCountWhatWasCharged_NotWhatWasDelivered()
    {
        await using var fixture = TestFixture.Create();
        var tenant = await fixture.AddTenantAsync("tenant-a", TenantCapability.Retail);
        var paidInKitchen = await fixture.AddProductAsync(tenant, "Torta pagada", UnitType.Unit, 50m);
        var deliveredUnpaid = await fixture.AddProductAsync(tenant, "Entregado sin pagar", UnitType.Unit, 50m);
        var paidBefore = await fixture.AddProductAsync(tenant, "Pagado antes", UnitType.Unit, 50m);
        // Paid in the range but not delivered yet (waiting for the kitchen): sold.
        var confirmed = fixture.NewOrder(tenant, paidInKitchen, quantity: 3);
        confirmed.TransitionTo(OrderStatus.Confirmed, new RetailOrderStateMachine(), "test");
        confirmed.RegisterPayment(confirmed.Total, OrderPaymentMethod.Cash, OrderDate, null, null, null, null);
        // Delivered on credit: nothing charged yet, so not counted.
        var unpaid = fixture.NewOrder(tenant, deliveredUnpaid, quantity: 5);
        unpaid.TransitionTo(OrderStatus.Delivered, new RetailOrderStateMachine(), "test");
        // Charged the week before (another range).
        var older = fixture.NewOrder(tenant, paidBefore, quantity: 7);
        older.RegisterPayment(older.Total, OrderPaymentMethod.Cash, OrderDate.AddDays(-7), null, null, null, null);
        fixture.Context.Orders.AddRange(confirmed, unpaid, older);
        await fixture.Context.SaveChangesAsync();

        var metrics = await fixture.GetMetricsAsync(tenant);

        var item = Assert.Single(metrics.TopSellingItems);
        Assert.Equal(paidInKitchen.Id, item.ProductId);
        Assert.Equal(3, item.QuantitySold);
    }

    [Fact]
    public async Task TopSellingItems_ShouldLeaveOutDishesCancelledFromATab()
    {
        await using var fixture = TestFixture.Create();
        var tenant = await fixture.AddTenantAsync("tenant-a", TenantCapability.Retail);
        var tacos = await fixture.AddProductAsync(tenant, "Tacos", UnitType.Unit, 50m);
        var tab = fixture.NewOrder(tenant, tacos, quantity: 3);
        tab.OpenAsTab(null, "Mesa 1", OrderDate, "mesero");
        tab.CancelTabItem(tab.Items.Single().Id, 1, "Se equivocó el mesero", returnToStock: true, OrderDate, "mesero");
        tab.RegisterPayment(tab.Total, OrderPaymentMethod.Cash, OrderDate, null, null, null, null);
        fixture.Context.Orders.Add(tab);
        await fixture.Context.SaveChangesAsync();

        var metrics = await fixture.GetMetricsAsync(tenant);

        var item = Assert.Single(metrics.TopSellingItems);
        Assert.Equal(2, item.QuantitySold);
        Assert.Equal(100m, item.TotalAmount);
    }

    /// <summary>
    /// «Cobrado» keeps its meaning and includes what the delivery platforms charged in their apps; PlatformCollected is that
    /// part, minus what went back as «Plataforma», and the platform rows of sales by channel add up to it (I8). An order a
    /// platform charged owes nothing; one whose courier has not paid the business yet is owed.
    /// </summary>
    [Fact]
    public async Task PlatformCollected_ShouldBeWhatThePlatformsCharged_MinusWhatWentBack()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, pricesIncludeTax: true);
        var pack = await fixture.AddProductAsync(tenant, "Paquete familiar", 1000m);
        var tacos = await fixture.AddProductAsync(tenant, "Orden de tacos", 245m);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.Rappi);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.DidiFood);
        await fixture.OpenCashSessionAsync(tenant);
        await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "R1", pack.Id));
        var cancelledByRappi = await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "R2", tacos.Id));
        await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.DidiFood, "D1", tacos.Id, collectedBy: PlatformCollectedBy.Business) with
        {
            Payments = [new CreateOrderPaymentDto { Amount = 245m, Method = OrderPaymentMethod.Cash }]
        });
        await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.DidiFood, "D2", tacos.Id, collectedBy: PlatformCollectedBy.Business));
        var voided = await new VoidOrderCommandHandler(
                fixture.Context, fixture.CurrentUser, new OrderCashService(fixture.Context, fixture.CurrentUser),
                fixture.Clock, NoOpOrderInventoryService.Instance)
            .Handle(new VoidOrderCommand { OrderId = cancelledByRappi.Id, Reason = "Rappi lo canceló", RestockItems = false }, CancellationToken.None);
        Assert.True(voided.IsSuccess, voided.Error);
        fixture.Context.ChangeTracker.Clear();

        var metrics = await new GetDashboardMetricsQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock)
            .Handle(new GetDashboardMetricsQuery(), CancellationToken.None);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(SalesTestFixture.Now, Common.Utils.TenantDateTimeResolver.Resolve(null)).DateTime);
        var byChannel = await new GetSalesByChannelQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock)
            .Handle(new GetSalesByChannelQuery { From = today, To = today }, CancellationToken.None);

        Assert.Equal(1000m, metrics.PlatformCollected);
        Assert.Equal(1000m + 245m + 245m - 245m, metrics.TotalRevenue);
        Assert.Equal(byChannel.Channels.Sum(c => c.PlatformCollected), metrics.PlatformCollected);
        Assert.Equal(byChannel.Totals.NetSales, metrics.TotalRevenue);
        // Only DiDi's second order is owed: the platform charged the others in full, or the courier paid.
        Assert.Equal((1, 245m), (metrics.UnpaidOrders, metrics.OutstandingBalance));
    }

    [Fact]
    public void Period_ShouldGoForwardAndSpanAtMostTheDaysOfTheReports()
    {
        var validator = new GetDashboardMetricsQueryValidator();

        Assert.True(validator.Validate(new GetDashboardMetricsQuery()).IsValid);
        Assert.True(validator.Validate(new GetDashboardMetricsQuery { FromDate = OrderDate.AddDays(-365), ToDate = OrderDate }).IsValid);
        var inverted = validator.Validate(new GetDashboardMetricsQuery { FromDate = OrderDate, ToDate = OrderDate.AddDays(-1) });
        Assert.Contains(inverted.Errors, e => e.ErrorMessage == "La fecha inicial debe ser anterior a la final.");
        var tooLong = validator.Validate(new GetDashboardMetricsQuery { FromDate = OrderDate.AddDays(-366), ToDate = OrderDate });
        Assert.Contains(tooLong.Errors, e => e.ErrorMessage == "El periodo puede abarcar como máximo 366 días.");
    }

    [Fact]
    public async Task Period_ShouldBeWholeDaysOfTheBusiness_AndAtMostAYearWithOnlyOneEnd()
    {
        await using var fixture = TestFixture.Create();
        var tenant = await fixture.AddTenantAsync("tenant-a", TenantCapability.Retail);
        fixture.TenantService.SetTenant(tenant.Id);
        var handler = new GetDashboardMetricsQueryHandler(fixture.Context, fixture.TenantService, new TestDateTimeProvider());

        // 18:00 UTC is 12:00 in Mexico City (UTC-6): each end stands for its whole local day.
        var day = await handler.Handle(
            new GetDashboardMetricsQuery { FromDate = OrderDate, ToDate = OrderDate },
            CancellationToken.None);
        var fromFarBack = await handler.Handle(
            new GetDashboardMetricsQuery { FromDate = OrderDate.AddYears(-3) },
            CancellationToken.None);

        Assert.Equal(new DateTimeOffset(2026, 2, 17, 6, 0, 0, TimeSpan.Zero), day.RangeFrom);
        Assert.Equal(new DateTimeOffset(2026, 2, 18, 6, 0, 0, TimeSpan.Zero).AddTicks(-1), day.RangeTo);
        Assert.Equal(new DateTimeOffset(2025, 2, 17, 6, 0, 0, TimeSpan.Zero), fromFarBack.RangeFrom);
        Assert.Equal(day.RangeTo, fromFarBack.RangeTo);
    }

    [Fact]
    public void TopSellingItemsQuery_ShouldFilterByTenantInSqlServerTranslation()
    {
        var tenantId = Guid.NewGuid();
        var tenantService = new TestCurrentTenantService();
        tenantService.SetTenant(tenantId);

        // SQL Server provider without a connection: ToQueryString() only translates the query.
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer()
            .Options;
        using var context = new ApplicationDbContext(options, tenantService, new NullMediator());
        var handler = new GetDashboardMetricsQueryHandler(context, tenantService, new TestDateTimeProvider());

        var sql = handler
            .BuildTopSellingItemsQuery(
                OrderDate.AddDays(-1),
                OrderDate.AddDays(1),
                canReadRetailOrders: true,
                canReadServiceOrders: true)
            .ToQueryString();
        _output.WriteLine(sql);

        // Global tenant filters must stay enabled for every tenant-scoped set in the query
        // (EF Core 10 names the filter parameter @ef_filter__CurrentTenantId).
        Assert.Matches(@"FROM \[Orders\] AS \[(\w+)\]\s+WHERE \[\1\]\.\[TenantId\] = @ef_filter__CurrentTenantId\d*\b", sql);
        Assert.Matches(@"FROM \[Products\] AS \[(\w+)\]\s+WHERE \[\1\]\.\[TenantId\] = @ef_filter__CurrentTenantId\d*\b", sql);

        // Explicit predicate on the orders (the alias carrying the status filter), bound to the current tenant.
        var ordersAlias = Regex.Match(sql, @"\[(\w+)\]\.\[Status\] <> 99");
        Assert.True(ordersAlias.Success, "Orders status predicate not found.");
        // Payments of the range are read with their own tenant filter.
        Assert.Matches(@"FROM \[OrderPayments\] AS \[(\w+)\]\s+WHERE \[\1\]\.\[TenantId\] = @ef_filter__CurrentTenantId\d*\b", sql);
        var explicitTenantPredicate = Regex.Match(
            sql,
            $@"\[{ordersAlias.Groups[1].Value}\]\.\[TenantId\] = @(?<parameter>(?!ef_filter__)\w+)");
        Assert.True(explicitTenantPredicate.Success, "Explicit tenant predicate on orders not found.");
        Assert.Matches(
            new Regex(
                $"DECLARE @{explicitTenantPredicate.Groups["parameter"].Value} uniqueidentifier = '{tenantId}';",
                RegexOptions.IgnoreCase),
            sql);
    }

    /// <summary>
    /// A sale in a context of its own, as in the app, stamped with its creation time like the database would (the
    /// in-memory fixture has no auditing interceptor).
    /// </summary>
    private static async Task<OrderDto> SellAsync(SalesTestFixture fixture, CreateOrderCommand command)
    {
        var sale = await fixture.CreateOrderHandler().Handle(command, CancellationToken.None);
        Assert.True(sale.IsSuccess, sale.Error);
        var order = await fixture.Context.Orders.SingleAsync(o => o.Id == sale.Value.Id);
        order.CreatedAt = fixture.Clock.UtcNow;
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        return sale.Value;
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private int _orderSequence;

        private TestFixture(TestApplicationDbContext context, TestCurrentTenantService tenantService)
        {
            Context = context;
            TenantService = tenantService;
        }

        public TestApplicationDbContext Context { get; }
        public TestCurrentTenantService TenantService { get; }

        public static TestFixture Create()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            var tenantService = new TestCurrentTenantService();
            var context = new TestApplicationDbContext(options, tenantService, new NullMediator());

            return new TestFixture(context, tenantService);
        }

        public async Task<Tenant> AddTenantAsync(string slug, TenantCapability capabilities)
        {
            var tenant = new Tenant(slug, slug, BusinessType.Retail, $"{slug}@tenant.com");
            Context.Tenants.Add(tenant);
            Context.TenantSettings.Add(new TenantSettings(tenant.Id, capabilities));
            await Context.SaveChangesAsync();
            return tenant;
        }

        public async Task<Product> AddProductAsync(Tenant tenant, string name, UnitType unit, decimal price)
        {
            var category = new Category(tenant.Id, "General", null);
            var product = new Product(tenant.Id, category.Id, name, null, price, "MXN", unit: unit);
            Context.Categories.Add(category);
            Context.Products.Add(product);
            await Context.SaveChangesAsync();
            return product;
        }

        public async Task<Order> AddCompletedOrderAsync(Tenant tenant, OrderType orderType, Product product, int quantity)
        {
            var order = new Order(
                tenantId: tenant.Id,
                customerUserId: null,
                temporaryCustomerId: Guid.NewGuid(),
                createdByBusiness: true,
                orderNumber: $"ORD-{++_orderSequence:D6}",
                businessType: tenant.BusinessType,
                orderType: orderType,
                customerName: "Cliente",
                customerPhone: "5511111111",
                customerNotes: null,
                tableNumber: null);
            order.AddItem(product.Id, product.Name, quantity, product.PriceAmount);
            // Paid in full: revenue counts registered payments.
            order.RegisterPayment(order.Total, OrderPaymentMethod.Cash, OrderDate, null, null, null, null);

            var completedStatus = orderType == OrderType.Service ? OrderStatus.Completed : OrderStatus.Delivered;
            order.TransitionTo(completedStatus, OrderStateMachineFactory.Create(tenant.BusinessType, orderType), "test");
            order.CreatedAt = OrderDate;

            Context.Orders.Add(order);
            await Context.SaveChangesAsync();
            return order;
        }

        public Order NewOrder(Tenant tenant, Product product, int quantity)
        {
            var order = new Order(tenant.Id, null, Guid.NewGuid(), true, $"ORD-{++_orderSequence:D6}", tenant.BusinessType,
                OrderType.Retail, "Cliente", "5511111111", null, null);
            order.AddItem(product.Id, product.Name, quantity, product.PriceAmount);
            order.CreatedAt = OrderDate;
            return order;
        }

        public Task<DashboardMetricsDto> GetMetricsAsync(Tenant tenant)
        {
            TenantService.SetTenant(tenant.Id);
            var handler = new GetDashboardMetricsQueryHandler(Context, TenantService, new TestDateTimeProvider());

            return handler.Handle(
                new GetDashboardMetricsQuery
                {
                    FromDate = OrderDate.AddDays(-1),
                    ToDate = OrderDate.AddDays(1)
                },
                CancellationToken.None);
        }

        public ValueTask DisposeAsync() => Context.DisposeAsync();
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
        public Guid TenantId { get; private set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    private sealed class TestDateTimeProvider : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = OrderDate;
    }

    private sealed class NullMediator : IMediator
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
            => Task.FromResult<TResponse>(default!);

        public Task Send<TRequest>(TRequest request, CancellationToken ct = default)
            where TRequest : IRequest
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
}
