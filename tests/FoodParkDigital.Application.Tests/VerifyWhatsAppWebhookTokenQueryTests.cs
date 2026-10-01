using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.WhatsApp.Queries;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

public class VerifyWhatsAppWebhookTokenQueryTests
{
    private const string TenantAToken = "a1a1a1a1a1a1a1a1a1a1a1a1";
    private const string TenantBToken = "b2b2b2b2b2b2b2b2b2b2b2b2";

    [Fact]
    public async Task Handle_ShouldReturnTenantThatOwnsTheToken()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var result = await new VerifyWhatsAppWebhookTokenQueryHandler(fixture.Context)
            .Handle(new VerifyWhatsAppWebhookTokenQuery(TenantBToken), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(fixture.TenantB, result.Value);
    }

    [Theory]
    [InlineData("unknown-token")]
    [InlineData("b2b2b2b2b2b2")]
    [InlineData("b2b2b2b2b2b2b2b2b2b2b2b2-suffix")]
    [InlineData("B2B2B2B2B2B2B2B2B2B2B2B2")]
    public async Task Handle_ShouldFail_WhenTokenDoesNotMatchExactly(string verifyToken)
    {
        await using var fixture = await TestFixture.CreateAsync();

        var result = await new VerifyWhatsAppWebhookTokenQueryHandler(fixture.Context)
            .Handle(new VerifyWhatsAppWebhookTokenQuery(verifyToken), CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private TestFixture(ApplicationDbContext context, Guid tenantB)
        {
            Context = context;
            TenantB = tenantB;
        }

        public ApplicationDbContext Context { get; }
        public Guid TenantB { get; }

        public static async Task<TestFixture> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            var context = new ApplicationDbContext(options, new TestCurrentTenantService(), new NullMediator());

            var tenantA = new Tenant("Tenant A", "tenant-a", BusinessType.Food, "a@tenant.com");
            var tenantB = new Tenant("Tenant B", "tenant-b", BusinessType.Retail, "b@tenant.com");
            var tenantWithoutToken = new Tenant("Tenant C", "tenant-c", BusinessType.Service, "c@tenant.com");
            context.Tenants.AddRange(tenantA, tenantB, tenantWithoutToken);

            context.WhatsAppConfigs.AddRange(
                CreateConfig(tenantA.Id, TenantAToken),
                CreateConfig(tenantB.Id, TenantBToken),
                new WhatsAppConfig(tenantWithoutToken.Id));
            await context.SaveChangesAsync();

            return new TestFixture(context, tenantB.Id);
        }

        public ValueTask DisposeAsync() => Context.DisposeAsync();

        private static WhatsAppConfig CreateConfig(Guid tenantId, string verifyToken)
        {
            var config = new WhatsAppConfig(tenantId);
            config.Update(false, null, null, null, null, verifyToken);
            return config;
        }
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
