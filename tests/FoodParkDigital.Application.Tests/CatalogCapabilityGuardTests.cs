using FoodParkDigital.Application.Catalog.Commands;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

public class CatalogCapabilityGuardTests
{
    [Fact]
    public async Task DeleteCategory_ShouldFail_WhenTenantLacksCatalogCapability()
    {
        await using var fixture = await TestFixture.CreateAsync(TenantCapability.Appointments);
        var category = await fixture.SeedCategoryAsync();

        var handler = new DeleteCategoryCommandHandler(fixture.Context);
        var result = await handler.Handle(new DeleteCategoryCommand(category.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("El tenant no tiene habilitado el modulo de catalogo.", result.Error);
    }

    [Fact]
    public async Task ToggleCategory_ShouldFail_WhenTenantLacksCatalogCapability()
    {
        await using var fixture = await TestFixture.CreateAsync(TenantCapability.Appointments);
        var category = await fixture.SeedCategoryAsync();

        var handler = new ToggleCategoryActiveCommandHandler(fixture.Context);
        var result = await handler.Handle(
            new ToggleCategoryActiveCommand
            {
                Id = category.Id,
                IsActive = false
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("El tenant no tiene habilitado el modulo de catalogo.", result.Error);
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly TestCurrentTenantService _tenantService;

        private TestFixture(ApplicationDbContext context, TestCurrentTenantService tenantService, Guid tenantId)
        {
            Context = context;
            _tenantService = tenantService;
            TenantId = tenantId;
        }

        public ApplicationDbContext Context { get; }
        public Guid TenantId { get; }

        public static async Task<TestFixture> CreateAsync(TenantCapability capabilities)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            var tenantService = new TestCurrentTenantService();
            var context = new ApplicationDbContext(options, tenantService, new NullMediator());

            var tenant = new Tenant("Catalog Guard", "catalog-guard", BusinessType.Food, "guard@test.com");
            context.Tenants.Add(tenant);
            context.TenantSettings.Add(new TenantSettings(tenant.Id, capabilities));
            await context.SaveChangesAsync();

            tenantService.SetTenant(tenant.Id);
            return new TestFixture(context, tenantService, tenant.Id);
        }

        public async Task<Category> SeedCategoryAsync()
        {
            _tenantService.SetTenant(TenantId);

            var category = new Category(TenantId, "General", null);
            Context.Categories.Add(category);
            await Context.SaveChangesAsync();
            return category;
        }

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed class TestCurrentTenantService : ICurrentTenantService
    {
        public Guid TenantId { get; private set; }
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
