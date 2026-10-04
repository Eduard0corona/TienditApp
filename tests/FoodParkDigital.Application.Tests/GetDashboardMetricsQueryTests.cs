using System.Text.RegularExpressions;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Orders.Queries;
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

        // Explicit predicate on the orders (the alias carrying the date range), bound to the current tenant.
        var ordersAlias = Regex.Match(sql, @"\[(\w+)\]\.\[CreatedAt\] >= @");
        Assert.True(ordersAlias.Success, "Orders date range predicate not found.");
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

            var completedStatus = orderType == OrderType.Service ? OrderStatus.Completed : OrderStatus.Delivered;
            order.TransitionTo(completedStatus, OrderStateMachineFactory.Create(tenant.BusinessType, orderType), "test");
            order.CreatedAt = OrderDate;

            Context.Orders.Add(order);
            await Context.SaveChangesAsync();
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
