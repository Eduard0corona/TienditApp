using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Persistence.Interceptors;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

public class TenantSaveChangesInterceptorTests
{
    [Fact]
    public async Task SaveChanges_ShouldStampCurrentTenant_WhenTenantIdIsEmpty()
    {
        var tenantA = Guid.NewGuid();
        await using var context = CreateContext(tenantA);

        var category = new Category(Guid.Empty, "Bebidas", null, 1);
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        Assert.Equal(tenantA, category.TenantId);
    }

    [Fact]
    public async Task SaveChanges_ShouldRejectRowsForAnotherTenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await using var context = CreateContext(tenantA);

        context.Categories.Add(new Category(tenantB, "Ajena", null, 1));

        await Assert.ThrowsAsync<TenantMismatchException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task SaveChanges_ShouldAllowRowsForTenantCreatedInSameUnitOfWork()
    {
        var currentTenant = Guid.NewGuid();
        await using var context = CreateContext(currentTenant);

        var newTenant = new Tenant("Nuevo", "nuevo", BusinessType.Retail, "nuevo@demo.com");
        context.Tenants.Add(newTenant);
        context.Customers.Add(new Customer(newTenant.Id, "Publico en general", null, null, isPublicGeneral: true));
        context.InventoryLocations.Add(new InventoryLocation(newTenant.Id, "Almacen principal", "MAIN", isDefault: true));

        await context.SaveChangesAsync();

        Assert.Equal(2, await context.Customers.IgnoreQueryFilters().CountAsync(c => c.TenantId == newTenant.Id)
            + await context.InventoryLocations.IgnoreQueryFilters().CountAsync(l => l.TenantId == newTenant.Id));
    }

    [Fact]
    public async Task SaveChanges_ShouldRejectChangingTenantIdOfPersistedRow()
    {
        var tenantA = Guid.NewGuid();
        await using var context = CreateContext(tenantA);
        var category = new Category(tenantA, "Bebidas", null, 1);
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        context.Entry(category).Property(nameof(Category.TenantId)).CurrentValue = Guid.NewGuid();

        await Assert.ThrowsAsync<TenantMismatchException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task SaveChanges_ShouldAcceptExplicitTenant_WhenThereIsNoTenantContext()
    {
        var tenantB = Guid.NewGuid();
        await using var context = CreateContext(Guid.Empty);

        context.Categories.Add(new Category(tenantB, "Webhook", null, 1));
        await context.SaveChangesAsync();

        Assert.Equal(1, await context.Categories.IgnoreQueryFilters().CountAsync(c => c.TenantId == tenantB));
    }

    private static ApplicationDbContext CreateContext(Guid tenantId)
    {
        var tenantService = new TestCurrentTenantService();
        tenantService.SetTenant(tenantId);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new TenantSaveChangesInterceptor(tenantService))
            .Options;
        return new ApplicationDbContext(options, tenantService, new NullMediator());
    }

    private sealed class TestCurrentTenantService : ICurrentTenantService
    {
        public Guid TenantId { get; private set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
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
