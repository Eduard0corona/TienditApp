using AutoMapper;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Mappings;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Finance.Entities;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests.Support;

/// <summary>
/// In-memory tenant with catalog, settings and cash session for the sales flow (create, charge, close).
/// InMemory proves the handlers' logic, not SQL Server semantics.
/// </summary>
internal sealed class SalesTestFixture : IAsyncDisposable
{
    public static readonly DateTimeOffset Now = new(2026, 10, 4, 17, 0, 0, TimeSpan.Zero);

    private SalesTestFixture(SalesDbContext context, SalesTenantService tenantService)
    {
        Context = context;
        TenantService = tenantService;
        Mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();
    }

    public SalesDbContext Context { get; }
    public IMapper Mapper { get; }
    /// <summary>The tenant the DbContext filters by.</summary>
    public SalesTenantService TenantService { get; }
    public SalesCurrentUser CurrentUser { get; } = new();
    public SalesClock Clock { get; } = new();

    public static SalesTestFixture Create()
    {
        var tenantService = new SalesTenantService();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SalesTestFixture(new SalesDbContext(options, tenantService), tenantService);
    }

    public async Task<Tenant> AddTenantAsync(
        BusinessType businessType,
        TenantCapability capabilities,
        bool pricesIncludeTax = false,
        bool autoCloseCounterSales = true,
        decimal defaultTaxRate = 16m,
        string slug = "demo")
    {
        var tenant = new Tenant($"Negocio {slug}", slug, businessType, $"{slug}@demo.com");
        var settings = new TenantSettings(tenant.Id, capabilities, pricesIncludeTax);
        settings.UpdateSalesPolicy(pricesIncludeTax, defaultTaxRate, autoCloseCounterSales);
        Context.Tenants.Add(tenant);
        Context.TenantSettings.Add(settings);
        Context.InventoryLocations.Add(new InventoryLocation(tenant.Id, "Mostrador", $"MAIN-{slug}", isDefault: true));
        await Context.SaveChangesAsync();
        TenantService.SetTenant(tenant.Id);
        return tenant;
    }

    public async Task<Product> AddProductAsync(
        Tenant tenant,
        string name,
        decimal price,
        decimal? taxRate = null,
        bool isTaxExempt = false)
    {
        var category = await Context.Categories.FirstOrDefaultAsync(c => c.TenantId == tenant.Id);
        if (category is null)
        {
            category = new Category(tenant.Id, "General", null);
            Context.Categories.Add(category);
        }

        var product = new Product(tenant.Id, category.Id, name, null, price, "MXN", taxRate: taxRate, isTaxExempt: isTaxExempt);
        Context.Products.Add(product);
        await Context.SaveChangesAsync();
        return product;
    }

    public async Task<CashSession> OpenCashSessionAsync(Tenant tenant)
    {
        var session = new CashSession(tenant.Id, Now.AddHours(-2), "cajero", 500m);
        Context.CashSessions.Add(session);
        await Context.SaveChangesAsync();
        return session;
    }

    public CreateOrderCommandHandler CreateOrderHandler()
    {
        var phoneNormalization = new PhoneNormalizationService();
        var customerLookup = new CustomerLookupService(
            Context,
            phoneNormalization,
            new HostingEnvironment { EnvironmentName = Environments.Production },
            NullLogger<CustomerLookupService>.Instance);

        return new CreateOrderCommandHandler(
            Context,
            TenantService,
            CurrentUser,
            Mapper,
            customerLookup,
            Clock,
            phoneNormalization,
            orderInventoryService: null,
            orderCashService: new OrderCashService(Context));
    }

    public AddOrderPaymentCommandHandler AddPaymentHandler()
        => new(Context, Clock, Mapper, new OrderCashService(Context), CurrentUser);

    public ValueTask DisposeAsync() => Context.DisposeAsync();
}

internal sealed class SalesDbContext : ApplicationDbContext
{
    public SalesDbContext(DbContextOptions<ApplicationDbContext> options, ICurrentTenantService tenantService)
        : base(options, tenantService, new SalesNullMediator())
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

internal sealed class SalesTenantService : ICurrentTenantService
{
    public Guid TenantId { get; private set; }
    public void SetTenant(Guid tenantId) => TenantId = tenantId;
}

internal sealed class SalesCurrentUser : ICurrentUserService
{
    public string? UserId { get; set; } = Guid.NewGuid().ToString();
    public string? UserName { get; set; } = "cajero";
    public string? Role { get; set; } = "TENANT_ADMIN";
    public string? TenantRole { get; set; } = "Owner";
    public IReadOnlyCollection<string> Permissions { get; set; } = [];
    public Guid? TenantId { get; set; }
}

internal sealed class SalesClock : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = SalesTestFixture.Now;
}

internal sealed class SalesNullMediator : IMediator
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
