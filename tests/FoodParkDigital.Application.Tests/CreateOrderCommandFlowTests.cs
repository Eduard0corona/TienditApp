using AutoMapper;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Mappings;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

public class CreateOrderCommandFlowTests
{
    [Fact]
    public async Task CreateOrder_ShouldLinkRegisteredCustomer_WhenPhoneExistsInCustomerProfile()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var (tenant, product) = await fixture.SeedCatalogAsync();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "registered@demo.com",
            NormalizedUserName = "REGISTERED@DEMO.COM",
            Email = "registered@demo.com",
            NormalizedEmail = "REGISTERED@DEMO.COM",
            FirstName = "Cliente",
            LastName = "Registrado",
            Role = UserRole.Customer,
            IsActive = true
        };
        fixture.Context.Users.Add(user);
        fixture.Context.CustomerProfiles.Add(
            new CustomerProfile(user.Id, "+525511112222", "Cliente Registrado", "Cliente", "Registrado"));
        await fixture.Context.SaveChangesAsync();

        fixture.SetTenant(tenant.Id);
        var handler = fixture.CreateHandler();
        var result = await handler.Handle(
            new CreateOrderCommand
            {
                CustomerPhone = "55 1111 2222",
                CustomerName = "Cliente Registrado",
                Items =
                [
                    new CreateOrderItemDto { ProductId = product.Id, Quantity = 1 }
                ]
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(user.Id, result.Value.CustomerUserId);
        Assert.Null(result.Value.TemporaryCustomerId);
        Assert.Empty(fixture.Context.TemporaryCustomers.IgnoreQueryFilters());
    }

    [Fact]
    public async Task CreateOrder_ShouldCreateTemporaryCustomer_WhenPhoneDoesNotExistInCustomerProfile()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var (tenant, product) = await fixture.SeedCatalogAsync();
        fixture.SetTenant(tenant.Id);

        var handler = fixture.CreateHandler();
        var result = await handler.Handle(
            new CreateOrderCommand
            {
                CustomerPhone = "55 2222 3333",
                CustomerName = "Cliente Temporal",
                Items =
                [
                    new CreateOrderItemDto { ProductId = product.Id, Quantity = 2 }
                ]
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.CustomerUserId);
        Assert.NotNull(result.Value.TemporaryCustomerId);

        var temp = await fixture.Context.TemporaryCustomers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == result.Value.TemporaryCustomerId);

        Assert.NotNull(temp);
        Assert.Equal(tenant.Id, temp!.TenantId);
        Assert.Equal("+525522223333", temp.PhoneNormalized);
        Assert.Equal("Cliente Temporal", temp.Name);
    }

    [Fact]
    public async Task CreateOrder_ShouldUseLegacyCustomerPhone_WhenCustomerIdIsProvided()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var (tenant, product) = await fixture.SeedCatalogAsync();
        fixture.SetTenant(tenant.Id);

        var publicGeneral = new Customer(
            tenant.Id,
            "Publico en general",
            "55 3333 4444",
            null,
            isPublicGeneral: true);
        fixture.Context.Customers.Add(publicGeneral);
        await fixture.Context.SaveChangesAsync();

        var handler = fixture.CreateHandler();
        var result = await handler.Handle(
            new CreateOrderCommand
            {
                CustomerId = publicGeneral.Id,
                Items =
                [
                    new CreateOrderItemDto { ProductId = product.Id, Quantity = 1 }
                ]
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Publico en general", result.Value.CustomerName);
        Assert.Equal("+525533334444", result.Value.CustomerPhone);
        Assert.NotNull(result.Value.TemporaryCustomerId);
    }

    [Fact]
    public async Task CreateOrder_ShouldFail_WhenSelectedCustomerHasNoPhoneAndNoOverride()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var (tenant, product) = await fixture.SeedCatalogAsync();
        fixture.SetTenant(tenant.Id);

        var customerWithoutPhone = new Customer(
            tenant.Id,
            "Cliente sin telefono",
            null,
            null,
            isPublicGeneral: false);
        fixture.Context.Customers.Add(customerWithoutPhone);
        await fixture.Context.SaveChangesAsync();

        var handler = fixture.CreateHandler();
        var result = await handler.Handle(
            new CreateOrderCommand
            {
                CustomerId = customerWithoutPhone.Id,
                Items =
                [
                    new CreateOrderItemDto { ProductId = product.Id, Quantity = 1 }
                ]
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("El cliente seleccionado no tiene telefono. Captura uno para continuar.", result.Error);
    }

    [Fact]
    public async Task CreateOrder_ShouldFail_WhenProductIsOutsideAvailabilitySchedule()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.SetUtcNow(new DateTimeOffset(2026, 2, 16, 12, 0, 0, TimeSpan.Zero)); // 06:00 in Mexico City
        var (tenant, _) = await fixture.SeedCatalogAsync();
        fixture.SetTenant(tenant.Id);

        var category = await fixture.Context.Categories.FirstAsync();
        var restrictedProduct = new Product(
            tenant.Id,
            category.Id,
            "Servicio Horario",
            null,
            120m,
            "MXN",
            unit: Domain.Catalog.Enums.UnitType.Service,
            availableFromTime: new TimeSpan(8, 0, 0),
            availableToTime: new TimeSpan(9, 0, 0));
        fixture.Context.Products.Add(restrictedProduct);
        await fixture.Context.SaveChangesAsync();

        var handler = fixture.CreateHandler();

        await Assert.ThrowsAsync<DomainException>(async () =>
            await handler.Handle(
                new CreateOrderCommand
                {
                    CustomerPhone = "55 9999 0000",
                    CustomerName = "Cliente Horario",
                    Items =
                    [
                        new CreateOrderItemDto { ProductId = restrictedProduct.Id, Quantity = 1 }
                    ]
                },
                CancellationToken.None));
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly TestCurrentTenantService _tenantService;
        private readonly TestCurrentUserService _currentUserService = new();
        private readonly TestDateTimeProvider _dateTimeProvider = new();

        private TestFixture(ApplicationDbContext context, IMapper mapper, TestCurrentTenantService tenantService)
        {
            Context = context;
            Mapper = mapper;
            _tenantService = tenantService;
        }

        public ApplicationDbContext Context { get; }
        public IMapper Mapper { get; }

        public static async Task<TestFixture> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            var tenantService = new TestCurrentTenantService();
            var context = new TestApplicationDbContext(options, tenantService, new NullMediator());

            var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>())
                .CreateMapper();

            var fixture = new TestFixture(context, mapper, tenantService);

            await Task.CompletedTask;
            return fixture;
        }

        public void SetTenant(Guid tenantId) => _tenantService.SetTenant(tenantId);
        public void SetUtcNow(DateTimeOffset utcNow) => _dateTimeProvider.UtcNow = utcNow;

        public CreateOrderCommandHandler CreateHandler()
        {
            var customerLookup = new CustomerLookupService(
                Context,
                new PhoneNormalizationService(),
                NullLogger<CustomerLookupService>.Instance);

            return new CreateOrderCommandHandler(
                Context,
                _tenantService,
                _currentUserService,
                Mapper,
                customerLookup,
                _dateTimeProvider);
        }

        public async Task<(Tenant Tenant, Product Product)> SeedCatalogAsync()
        {
            var tenant = new Tenant("Demo Tenant", "demo-tenant", BusinessType.Food, "demo@tenant.com");
            Context.Tenants.Add(tenant);
            await Context.SaveChangesAsync();

            SetTenant(tenant.Id);

            var category = new Category(tenant.Id, "General", null);
            Context.Categories.Add(category);
            await Context.SaveChangesAsync();

            var product = new Product(
                tenant.Id,
                category.Id,
                "Producto Demo",
                null,
                100m,
                "MXN");
            Context.Products.Add(product);
            await Context.SaveChangesAsync();

            return (tenant, product);
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
        public string? UserName => "test-user";
        public string? Role => "TENANT_ADMIN";
        public string? TenantRole => "Admin";
        public Guid? TenantId => null;
    }

    private sealed class TestDateTimeProvider : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = new DateTimeOffset(2026, 2, 16, 16, 0, 0, TimeSpan.Zero);
    }

    private class NullMediator : IMediator
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
