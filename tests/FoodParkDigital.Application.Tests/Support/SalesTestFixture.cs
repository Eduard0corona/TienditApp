using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.Finance;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Branches.Entities;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Finance.Entities;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Domain.WhatsApp.Entities;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
using FoodParkDigital.Application.Tests.Support;
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
    }

    public SalesDbContext Context { get; }
    /// <summary>The tenant the DbContext filters by.</summary>
    public SalesTenantService TenantService { get; }
    public SalesCurrentUser CurrentUser { get; } = new();
    public SalesClock Clock { get; } = new();

    /// <param name="mediator">Hears the events of what is saved (none by default).</param>
    public static SalesTestFixture Create(IMediator? mediator = null)
    {
        var tenantService = new SalesTenantService();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SalesTestFixture(new SalesDbContext(options, tenantService, mediator), tenantService);
    }

    /// <summary>
    /// A delivery platform of the tenant in scope, with the fiscal defaults (IVA 16 % on the commission, ISR 2.5 % and
    /// half the IVA withheld) and the commission of its contract.
    /// </summary>
    public async Task<DeliveryPlatformSetting> AddDeliveryPlatformAsync(
        Tenant tenant,
        OrderChannel channel,
        decimal commissionPercent = 30m,
        bool isActive = true,
        PlatformCommissionBase commissionBase = PlatformCommissionBase.TotalWithTax)
    {
        var setting = new DeliveryPlatformSetting(
            tenant.Id, channel, isActive, PlatformTerms.WithFiscalDefaults(commissionPercent, commissionBase));
        Context.DeliveryPlatformSettings.Add(setting);
        await Context.SaveChangesAsync();
        return setting;
    }

    /// <summary>An order of a delivery platform as Vender sends it: its folio, who charged it and the name for the bag.</summary>
    public static CreateOrderCommand PlatformOrder(
        OrderChannel channel,
        string folio,
        Guid productId,
        decimal quantity = 1,
        PlatformCollectedBy? collectedBy = null,
        string? bagName = null) => new()
    {
        Channel = channel,
        ExternalOrderNumber = folio,
        CollectedBy = collectedBy,
        CustomerName = bagName,
        Items = [new CreateOrderItemDto { ProductId = productId, Quantity = quantity }]
    };

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
        // Like a business after the branches migration: a default branch that owns the default location.
        var branch = new Branch(tenant.Id, "Matriz", $"MATRIZ-{slug}", isDefault: true);
        Context.Branches.Add(branch);
        Context.InventoryLocations.Add(new InventoryLocation(tenant.Id, "Mostrador", $"MAIN-{slug}", isDefault: true, branchId: branch.Id));
        await Context.SaveChangesAsync();
        TenantService.SetTenant(tenant.Id);
        return tenant;
    }

    /// <summary>The default branch of the tenant in scope.</summary>
    public Task<Branch> DefaultBranchAsync() => Context.Branches.SingleAsync(b => b.IsDefault);

    /// <summary>Another branch of the tenant in scope, with its own stock location.</summary>
    public async Task<Branch> AddBranchAsync(Tenant tenant, string name, string code)
    {
        var branch = new Branch(tenant.Id, name, code);
        Context.Branches.Add(branch);
        Context.InventoryLocations.Add(new InventoryLocation(tenant.Id, $"Almacén {name}", $"LOC-{code}", branchId: branch.Id));
        await Context.SaveChangesAsync();
        return branch;
    }

    public async Task<Product> AddProductAsync(
        Tenant tenant,
        string name,
        decimal price,
        decimal? taxRate = null,
        bool isTaxExempt = false,
        UnitType? unit = null)
    {
        var category = await Context.Categories.FirstOrDefaultAsync(c => c.TenantId == tenant.Id);
        if (category is null)
        {
            category = new Category(tenant.Id, "General", null);
            Context.Categories.Add(category);
        }

        var product = new Product(tenant.Id, category.Id, name, null, price, "MXN", taxRate: taxRate, unit: unit, isTaxExempt: isTaxExempt);
        Context.Products.Add(product);
        await Context.SaveChangesAsync();
        return product;
    }

    public async Task<CashSession> OpenCashSessionAsync(Tenant tenant, Guid? branchId = null, Guid? openedByUserId = null)
    {
        var session = new CashSession(tenant.Id, Now.AddHours(-2), "cajero", 500m, openedByUserId: openedByUserId, branchId: branchId);
        Context.CashSessions.Add(session);
        await Context.SaveChangesAsync();
        return session;
    }

    public CreateOrderCommandHandler CreateOrderHandler(IOrderInventoryService? orderInventoryService = null)
    {
        var phoneNormalization = new PhoneNormalizationService();
        var customerLookup = new CustomerLookupService(
            Context,
            phoneNormalization,
            NullLogger<CustomerLookupService>.Instance);

        return new CreateOrderCommandHandler(
            Context,
            TenantService,
            CurrentUser,
            customerLookup,
            Clock,
            phoneNormalization,
            orderInventoryService: orderInventoryService ?? NoOpOrderInventoryService.Instance,
            orderCashService: new OrderCashService(Context, CurrentUser),
            folios: new FolioGenerator(Context));
    }

    public AddOrderPaymentCommandHandler AddPaymentHandler()
        => new(Context, Clock, new OrderCashService(Context, CurrentUser), CurrentUser);

    public ValueTask DisposeAsync() => Context.DisposeAsync();
}

internal sealed class SalesDbContext : ApplicationDbContext
{
    public SalesDbContext(DbContextOptions<ApplicationDbContext> options, ICurrentTenantService tenantService, IMediator? mediator = null)
        : base(options, tenantService, mediator ?? new SalesNullMediator())
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
        modelBuilder.Entity<Appointment>()
            .Property(a => a.Version)
            .IsRequired(false)
            .ValueGeneratedNever();
        modelBuilder.Entity<WhatsAppConversation>()
            .Property<byte[]>("Version")
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
