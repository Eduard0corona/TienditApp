using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

public class TenantAccessServiceTests
{
    [Fact]
    public async Task HasCapabilityAsync_ShouldReturnTrue_WhenTenantHasRequiredCapability()
    {
        var tenantService = new TestCurrentTenantService();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options, tenantService, new NullMediator());
        var tenant = new Tenant("Tenant A", "tenant-a", BusinessType.Service, "tenant-a@test.com");
        context.Tenants.Add(tenant);
        context.TenantSettings.Add(new TenantSettings(
            tenant.Id,
            TenantCapability.Service | TenantCapability.Inventory));
        await context.SaveChangesAsync();

        var service = new TenantAccessService(context);
        var result = await service.HasCapabilityAsync(tenant.Id, TenantCapability.Inventory, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task HasAnyCapabilityAsync_ShouldReturnFalse_WhenTenantHasNoMatchingCapability()
    {
        var tenantService = new TestCurrentTenantService();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options, tenantService, new NullMediator());
        var tenant = new Tenant("Tenant B", "tenant-b", BusinessType.Retail, "tenant-b@test.com");
        context.Tenants.Add(tenant);
        context.TenantSettings.Add(new TenantSettings(tenant.Id, TenantCapability.Appointments));
        await context.SaveChangesAsync();

        var service = new TenantAccessService(context);
        var result = await service.HasAnyCapabilityAsync(
            tenant.Id,
            TenantCapability.Retail | TenantCapability.Service | TenantCapability.Food,
            CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task HasAnyCapabilityAsync_ShouldUseBusinessTypeFallback_WhenCapabilitiesAreLegacyInventoryOnly()
    {
        var tenantService = new TestCurrentTenantService();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options, tenantService, new NullMediator());
        var tenant = new Tenant("Tenant Legacy", "tenant-legacy", BusinessType.Food, "tenant-legacy@test.com");
        context.Tenants.Add(tenant);
        context.TenantSettings.Add(new TenantSettings(tenant.Id, TenantCapability.Inventory));
        await context.SaveChangesAsync();

        var service = new TenantAccessService(context);
        var result = await service.HasAnyCapabilityAsync(
            tenant.Id,
            TenantCapability.Food | TenantCapability.Retail | TenantCapability.Service,
            CancellationToken.None);

        Assert.True(result);
    }

    private sealed class TestCurrentTenantService : ICurrentTenantService
    {
        public Guid TenantId { get; private set; } = Guid.NewGuid();

        public void SetTenant(Guid tenantId) => TenantId = tenantId;
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
