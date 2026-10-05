using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Tenants.Commands;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

public class UpdateMyTenantCommandTests
{
    [Fact]
    public async Task Handle_ShouldCreateAuditLog_WhenCapabilitiesChange()
    {
        await using var fixture = await TestFixture.CreateAsync(TenantCapability.Inventory);

        var handler = new UpdateMyTenantCommandHandler(
            fixture.Context,
            fixture.CurrentUser,
            fixture.TenantService);

        var result = await handler.Handle(
            new UpdateMyTenantCommand
            {
                Name = "Tenant Demo",
                ContactEmail = "tenant@demo.com",
                Capabilities = TenantCapability.Food | TenantCapability.Inventory
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(TenantCapability.Food | TenantCapability.Inventory, result.Value.Capabilities);

        var auditLog = await fixture.Context.AuditLogs.SingleAsync();
        Assert.Equal("TenantCapabilitiesUpdated", auditLog.Action);
        Assert.Equal(fixture.Tenant.Id.ToString(), auditLog.EntityId);
    }

    [Fact]
    public async Task Handle_ShouldNotCreateAuditLog_WhenCapabilitiesDoNotChange()
    {
        await using var fixture = await TestFixture.CreateAsync(TenantCapability.Food | TenantCapability.Inventory);

        var handler = new UpdateMyTenantCommandHandler(
            fixture.Context,
            fixture.CurrentUser,
            fixture.TenantService);

        var result = await handler.Handle(
            new UpdateMyTenantCommand
            {
                Name = "Tenant Demo",
                ContactEmail = "tenant@demo.com",
                Capabilities = TenantCapability.Food | TenantCapability.Inventory
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(fixture.Context.AuditLogs);
    }

    [Fact]
    public async Task Handle_ShouldFail_WhenNoRequestTenantAndNoActiveMembership()
    {
        await using var fixture = await TestFixture.CreateAsync(TenantCapability.Food);
        var emptyTenantService = new TestCurrentTenantService();

        var handler = new UpdateMyTenantCommandHandler(
            fixture.Context,
            fixture.CurrentUser,
            emptyTenantService);

        var result = await handler.Handle(
            new UpdateMyTenantCommand
            {
                Name = "Nombre cambiado",
                ContactEmail = "tenant@demo.com"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        var tenant = await fixture.Context.Tenants.SingleAsync();
        Assert.Equal("Tenant Demo", tenant.Name);
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private TestFixture(
            ApplicationDbContext context,
            TestCurrentUserService currentUser,
            TestCurrentTenantService tenantService,
            Tenant tenant)
        {
            Context = context;
            CurrentUser = currentUser;
            TenantService = tenantService;
            Tenant = tenant;
        }

        public ApplicationDbContext Context { get; }
        public TestCurrentUserService CurrentUser { get; }
        public TestCurrentTenantService TenantService { get; }
        public Tenant Tenant { get; }

        public static async Task<TestFixture> CreateAsync(TenantCapability capabilities)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            var tenantService = new TestCurrentTenantService();
            var context = new ApplicationDbContext(options, tenantService, new NullMediator());

            var tenant = new Tenant("Tenant Demo", "tenant-demo", BusinessType.Food, "tenant@demo.com");
            context.Tenants.Add(tenant);
            context.TenantSettings.Add(new TenantSettings(tenant.Id, capabilities));
            await context.SaveChangesAsync();

            tenantService.SetTenant(tenant.Id);


            var currentUser = new TestCurrentUserService(tenant.Id);
            return new TestFixture(context, currentUser, tenantService, tenant);
        }

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed class TestCurrentUserService(Guid tenantId) : ICurrentUserService
    {
        public string? UserId => Guid.NewGuid().ToString();
        public string? UserName => "tenant-owner";
        public string? Role => "TENANT_ADMIN";
        public string? TenantRole => "OWNER";
        public IReadOnlyCollection<string> Permissions => [];
        public Guid? TenantId => tenantId;
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
