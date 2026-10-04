using AutoMapper;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Mappings;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

public class CoreBusinessFlowSmokeTests
{
    [Fact]
    [Trait("Category", "Smoke")]
    public async Task FoodTenant_OrderLifecycle_ShouldWork_EndToEnd()
    {
        await using var fixture = await SmokeFixture.CreateAsync();
        var seeded = await fixture.SeedAsync();
        fixture.SetTenant(seeded.Tenant.Id);

        var createHandler = fixture.CreateOrderHandler();
        var createResult = await createHandler.Handle(
            new CreateOrderCommand
            {
                CustomerPhone = "55 1111 2222",
                CustomerName = "Cliente Smoke",
                Items =
                [
                    new CreateOrderItemDto
                    {
                        ProductId = seeded.Product.Id,
                        Quantity = 2
                    }
                ]
            },
            CancellationToken.None);

        Assert.True(createResult.IsSuccess, createResult.Error);
        Assert.Equal(OrderStatus.Confirmed, createResult.Value.Status);
        Assert.Equal(seeded.RegisteredCustomer.Id, createResult.Value.CustomerUserId);
        Assert.Null(createResult.Value.TemporaryCustomerId);
        Assert.Single(await fixture.Context.InventoryMovements.ToListAsync());

        var persistedOrder = await fixture.Context.Orders
            .Include(o => o.StatusHistory)
            .FirstAsync(o => o.Id == createResult.Value.Id);

        // Tests use direct DbContext construction without save interceptors; stamp creation time manually.
        persistedOrder.CreatedAt = fixture.UtcNow;
        await fixture.Context.SaveChangesAsync();

        var updateStatusHandler = fixture.CreateUpdateStatusHandler();
        var updateResult = await updateStatusHandler.Handle(
            new UpdateOrderStatusCommand
            {
                OrderId = persistedOrder.Id,
                NewStatus = OrderStatus.Delivered
            },
            CancellationToken.None);

        Assert.True(updateResult.IsSuccess, updateResult.Error);

        var deliveredOrder = await fixture.Context.Orders
            .Include(o => o.StatusHistory)
            .FirstAsync(o => o.Id == persistedOrder.Id);

        Assert.Equal(OrderStatus.Delivered, deliveredOrder.Status);
        Assert.Equal(3, deliveredOrder.StatusHistory.Count);
        Assert.Single(await fixture.Context.InventoryMovements.ToListAsync());

        var stockAfterDelivery = await fixture.Context.InventoryStocks
            .SingleAsync(x => x.SupplyItemId == seeded.Supply.Id && x.InventoryLocationId == seeded.Location.Id);
        Assert.Equal(8m, stockAfterDelivery.OnHandQuantity);

        var dashboardHandler = fixture.CreateDashboardHandler();
        var metrics = await dashboardHandler.Handle(new GetDashboardMetricsQuery(), CancellationToken.None);

        Assert.Equal(1, metrics.TotalOrders);
        Assert.Equal(1, metrics.CompletedOrders);
        Assert.Equal(1, metrics.ActiveProducts);
        Assert.Equal(0, metrics.ActiveServices);
        Assert.Single(metrics.TopSellingItems);
        Assert.Equal(seeded.Product.Id, metrics.TopSellingItems[0].ProductId);
        Assert.Equal("Product", metrics.TopSellingItems[0].ItemType);
        Assert.True(metrics.TotalRevenue > 0);
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task FoodTenant_CancelOrder_ShouldReverseInventoryConsumption()
    {
        await using var fixture = await SmokeFixture.CreateAsync();
        var seeded = await fixture.SeedAsync();
        fixture.SetTenant(seeded.Tenant.Id);

        var createHandler = fixture.CreateOrderHandler();
        var createResult = await createHandler.Handle(
            new CreateOrderCommand
            {
                CustomerPhone = "55 1111 2222",
                CustomerName = "Cliente Smoke",
                Items =
                [
                    new CreateOrderItemDto
                    {
                        ProductId = seeded.Product.Id,
                        Quantity = 3
                    }
                ]
            },
            CancellationToken.None);

        Assert.True(createResult.IsSuccess, createResult.Error);

        var stockAfterCreate = await fixture.Context.InventoryStocks
            .SingleAsync(x => x.SupplyItemId == seeded.Supply.Id && x.InventoryLocationId == seeded.Location.Id);
        Assert.Equal(7m, stockAfterCreate.OnHandQuantity);

        var cancelHandler = fixture.CreateCancelHandler();
        var cancelResult = await cancelHandler.Handle(
            new CancelOrderCommand
            {
                OrderId = createResult.Value.Id,
                Reason = "Cancelacion de prueba"
            },
            CancellationToken.None);

        Assert.True(cancelResult.IsSuccess, cancelResult.Error);

        var cancelledOrder = await fixture.Context.Orders.FirstAsync(x => x.Id == createResult.Value.Id);
        Assert.Equal(OrderStatus.Cancelled, cancelledOrder.Status);

        var stockAfterCancel = await fixture.Context.InventoryStocks
            .SingleAsync(x => x.SupplyItemId == seeded.Supply.Id && x.InventoryLocationId == seeded.Location.Id);
        Assert.Equal(10m, stockAfterCancel.OnHandQuantity);

        var movements = await fixture.Context.InventoryMovements
            .OrderBy(x => x.MovementDate)
            .ToListAsync();

        Assert.Equal(2, movements.Count);
        Assert.Contains(movements, x => x.ExternalReference == $"ORDER:{createResult.Value.Id:N}:CONSUMPTION");
        Assert.Contains(movements, x => x.ExternalReference == $"ORDER:{createResult.Value.Id:N}:REVERSAL");
    }

    private sealed class SmokeFixture : IAsyncDisposable
    {
        private readonly TestCurrentTenantService _tenantService;
        private readonly TestCurrentUserService _currentUserService = new();
        private readonly TestDateTimeProvider _dateTimeProvider = new();

        private SmokeFixture(
            TestApplicationDbContext context,
            IMapper mapper,
            TestCurrentTenantService tenantService)
        {
            Context = context;
            Mapper = mapper;
            _tenantService = tenantService;
        }

        public TestApplicationDbContext Context { get; }
        public IMapper Mapper { get; }
        public DateTimeOffset UtcNow => _dateTimeProvider.UtcNow;

        public static async Task<SmokeFixture> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            var tenantService = new TestCurrentTenantService();
            var context = new TestApplicationDbContext(options, tenantService, new NullMediator());
            var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>())
                .CreateMapper();

            var fixture = new SmokeFixture(context, mapper, tenantService);
            await Task.CompletedTask;
            return fixture;
        }

        public void SetTenant(Guid tenantId) => _tenantService.SetTenant(tenantId);

        public CreateOrderCommandHandler CreateOrderHandler()
        {
            var customerLookup = new CustomerLookupService(
                Context,
                new PhoneNormalizationService(),
                new HostingEnvironment { EnvironmentName = Environments.Production },
                NullLogger<CustomerLookupService>.Instance);
            var orderInventoryService = new OrderInventoryService(
                Context,
                _dateTimeProvider,
                NullLogger<OrderInventoryService>.Instance);

            return new CreateOrderCommandHandler(
                Context,
                _tenantService,
                _currentUserService,
                Mapper,
                customerLookup,
                _dateTimeProvider,
                new PhoneNormalizationService(),
                orderInventoryService);
        }

        public UpdateOrderStatusCommandHandler CreateUpdateStatusHandler()
            => new(
                Context,
                _currentUserService,
                new OrderInventoryService(
                    Context,
                    _dateTimeProvider,
                    NullLogger<OrderInventoryService>.Instance));

        public CancelOrderCommandHandler CreateCancelHandler()
            => new(
                Context,
                _currentUserService,
                new OrderInventoryService(
                    Context,
                    _dateTimeProvider,
                    NullLogger<OrderInventoryService>.Instance));

        public GetDashboardMetricsQueryHandler CreateDashboardHandler()
            => new(Context, _tenantService, _dateTimeProvider);

        public async Task<SeededData> SeedAsync()
        {
            var tenant = new Tenant("Smoke Tenant", "smoke-tenant", BusinessType.Food, "smoke@tenant.com");
            Context.Tenants.Add(tenant);
            await Context.SaveChangesAsync();

            Context.TenantSettings.Add(new TenantSettings(
                tenant.Id,
                TenantCapabilityDefaults.FromBusinessType(tenant.BusinessType) | TenantCapability.Inventory));
            await Context.SaveChangesAsync();

            SetTenant(tenant.Id);

            var category = new Category(tenant.Id, "General", null);
            Context.Categories.Add(category);
            await Context.SaveChangesAsync();

            var product = new Product(
                tenant.Id,
                category.Id,
                "Hamburguesa Smoke",
                "Producto principal",
                150m,
                "MXN",
                unit: UnitType.Unit);

            var service = new Product(
                tenant.Id,
                category.Id,
                "Asesoria Smoke",
                "Servicio para contar en dashboard",
                300m,
                "MXN",
                unit: UnitType.Service);

            Context.Products.AddRange(product, service);

            var supply = new SupplyItem(tenant.Id, "Detergente", UnitType.Liter, code: "SUP-001");
            var location = new InventoryLocation(tenant.Id, "Almacen Principal", "MAIN", isDefault: true);
            Context.SupplyItems.Add(supply);
            Context.InventoryLocations.Add(location);

            var registeredCustomer = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "smoke.customer@demo.com",
                NormalizedUserName = "SMOKE.CUSTOMER@DEMO.COM",
                Email = "smoke.customer@demo.com",
                NormalizedEmail = "SMOKE.CUSTOMER@DEMO.COM",
                FirstName = "Cliente",
                LastName = "Smoke",
                PhoneNumber = "+525511112222",
                PhoneNumberConfirmed = true,
                Role = UserRole.Customer,
                IsActive = true
            };

            Context.Users.Add(registeredCustomer);
            Context.CustomerProfiles.Add(
                new CustomerProfile(
                    registeredCustomer.Id,
                    "+525511112222",
                    "Cliente Smoke",
                    "Cliente",
                    "Smoke"));

            await Context.SaveChangesAsync();

            var stock = new InventoryStock(tenant.Id, supply.Id, location.Id);
            stock.ApplyMovement(10, 50, _dateTimeProvider.UtcNow);
            Context.InventoryStocks.Add(stock);

            Context.ProductSupplyRequirements.Add(new ProductSupplyRequirement(
                tenantId: tenant.Id,
                productId: product.Id,
                supplyItemId: supply.Id,
                quantity: 1,
                inventoryLocationId: location.Id,
                wastePercent: 0));

            await Context.SaveChangesAsync();

            return new SeededData(tenant, product, service, registeredCustomer, supply, location);
        }

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed record SeededData(
        Tenant Tenant,
        Product Product,
        Product Service,
        ApplicationUser RegisteredCustomer,
        SupplyItem Supply,
        InventoryLocation Location);

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

            // InMemory provider does not generate rowversion; allow test inserts.
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

    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public string? UserId => Guid.NewGuid().ToString();
        public string? UserName => "smoke-user";
        public string? Role => "TENANT_ADMIN";
        public string? TenantRole => "Admin";
        public IReadOnlyCollection<string> Permissions => [];
        public Guid? TenantId => null;
    }

    private sealed class TestDateTimeProvider : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 2, 16, 16, 0, 0, TimeSpan.Zero);
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

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken ct = default)
            => AsyncEnumerable.Empty<TResponse>();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            object request,
            CancellationToken ct = default)
            => AsyncEnumerable.Empty<TResponse>();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken ct = default)
            => AsyncEnumerable.Empty<object?>();

        public Task<object?> Send(object request, CancellationToken ct = default)
            => Task.FromResult<object?>(null);
    }
}
