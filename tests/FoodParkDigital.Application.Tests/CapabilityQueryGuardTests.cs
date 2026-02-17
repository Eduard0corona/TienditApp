using AutoMapper;
using FoodParkDigital.Application.Catalog.Queries;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Mappings;
using FoodParkDigital.Application.Inventory.Queries;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

public class CapabilityQueryGuardTests
{
    [Fact]
    public async Task GetProducts_ShouldHideServices_WhenTenantHasNoServiceCapability()
    {
        await using var fixture = await TestFixture.CreateAsync(BusinessType.Retail, TenantCapability.Retail | TenantCapability.Inventory);
        await fixture.SeedCatalogAsync();

        var handler = new GetProductsQueryHandler(
            fixture.Context,
            fixture.TenantService,
            new TestDateTimeProvider(),
            fixture.Mapper);

        var allItems = await handler.Handle(new GetProductsQuery(), CancellationToken.None);
        Assert.NotEmpty(allItems.Items);
        Assert.DoesNotContain(allItems.Items, x => x.Unit is UnitType.Hour or UnitType.Session or UnitType.Service);

        var services = await handler.Handle(
            new GetProductsQuery { ItemType = ProductCatalogItemType.Service },
            CancellationToken.None);
        Assert.Empty(services.Items);
    }

    [Fact]
    public async Task GetOrdersByBusiness_ShouldReturnOnlyAllowedOrderType()
    {
        await using var fixture = await TestFixture.CreateAsync(BusinessType.Service, TenantCapability.Service | TenantCapability.Inventory);
        await fixture.SeedOrdersAsync();

        var handler = new GetOrdersByBusinessQueryHandler(fixture.Context, fixture.TenantService, fixture.Mapper);
        var result = await handler.Handle(new GetOrdersByBusinessQuery(), CancellationToken.None);

        Assert.NotEmpty(result.Items);
        Assert.All(result.Items, x => Assert.Equal(OrderType.Service, x.OrderType));
    }

    [Fact]
    public async Task GetSupplyItems_ShouldReturnEmpty_WhenInventoryCapabilityIsMissing()
    {
        await using var fixture = await TestFixture.CreateAsync(BusinessType.Retail, TenantCapability.Retail);
        await fixture.SeedSupplyAsync();

        var handler = new GetSupplyItemsQueryHandler(fixture.Context, fixture.TenantService);
        var result = await handler.Handle(new GetSupplyItemsQuery(), CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private TestFixture(TestApplicationDbContext context, IMapper mapper, TestCurrentTenantService tenantService, Tenant tenant)
        {
            Context = context;
            Mapper = mapper;
            TenantService = tenantService;
            Tenant = tenant;
        }

        public TestApplicationDbContext Context { get; }
        public IMapper Mapper { get; }
        public TestCurrentTenantService TenantService { get; }
        public Tenant Tenant { get; }

        public static async Task<TestFixture> CreateAsync(BusinessType businessType, TenantCapability capabilities)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            var tenantService = new TestCurrentTenantService();
            var context = new TestApplicationDbContext(options, tenantService, new NullMediator());

            var tenant = new Tenant("Capability Tenant", "capability-tenant", businessType, "capability@tenant.com");
            context.Tenants.Add(tenant);
            context.TenantSettings.Add(new TenantSettings(tenant.Id, capabilities));
            await context.SaveChangesAsync();

            tenantService.SetTenant(tenant.Id);

            var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>())
                .CreateMapper();

            return new TestFixture(context, mapper, tenantService, tenant);
        }

        public async Task SeedCatalogAsync()
        {
            var category = new Category(Tenant.Id, "General", null);
            Context.Categories.Add(category);
            await Context.SaveChangesAsync();

            Context.Products.Add(new Product(Tenant.Id, category.Id, "Producto", null, 100m, "MXN", unit: UnitType.Unit));
            Context.Products.Add(new Product(Tenant.Id, category.Id, "Servicio", null, 120m, "MXN", unit: UnitType.Service));
            await Context.SaveChangesAsync();
        }

        public async Task SeedOrdersAsync()
        {
            var category = new Category(Tenant.Id, "General", null);
            Context.Categories.Add(category);
            await Context.SaveChangesAsync();

            var product = new Product(Tenant.Id, category.Id, "Producto", null, 100m, "MXN", unit: UnitType.Unit);
            var service = new Product(Tenant.Id, category.Id, "Servicio", null, 120m, "MXN", unit: UnitType.Service);
            Context.Products.AddRange(product, service);
            await Context.SaveChangesAsync();

            var retailOrder = new Order(
                tenantId: Tenant.Id,
                customerUserId: null,
                temporaryCustomerId: Guid.NewGuid(),
                createdByBusiness: true,
                orderNumber: "R-1",
                businessType: BusinessType.Service,
                orderType: OrderType.Retail,
                customerName: "Retail",
                customerPhone: "5511111111",
                customerNotes: null,
                tableNumber: null);
            retailOrder.AddItem(product.Id, product.Name, 1, 100m);

            var serviceOrder = new Order(
                tenantId: Tenant.Id,
                customerUserId: null,
                temporaryCustomerId: Guid.NewGuid(),
                createdByBusiness: true,
                orderNumber: "S-1",
                businessType: BusinessType.Service,
                orderType: OrderType.Service,
                customerName: "Service",
                customerPhone: "5522222222",
                customerNotes: null,
                tableNumber: null);
            serviceOrder.AddItem(service.Id, service.Name, 1, 120m);

            Context.Orders.AddRange(retailOrder, serviceOrder);
            await Context.SaveChangesAsync();
        }

        public async Task SeedSupplyAsync()
        {
            Context.SupplyItems.Add(new SupplyItem(
                tenantId: Tenant.Id,
                name: "Detergente",
                unit: UnitType.Liter,
                defaultCost: 20m));
            await Context.SaveChangesAsync();
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
        public DateTimeOffset UtcNow { get; } = new(2026, 2, 17, 12, 0, 0, TimeSpan.Zero);
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
