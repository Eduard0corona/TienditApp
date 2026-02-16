using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace FoodParkDigital.Application.Tests;

public class EfModelConfigurationTests
{
    [Fact]
    public async Task BaseEntityId_ShouldBeConfiguredAsValueGeneratedNever()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options, new TestCurrentTenantService(), new NullMediator());

        var baseEntityTypes = context.Model.GetEntityTypes()
            .Where(x => typeof(BaseEntity).IsAssignableFrom(x.ClrType))
            .ToList();

        Assert.NotEmpty(baseEntityTypes);

        foreach (var entityType in baseEntityTypes)
        {
            var idProperty = entityType.FindProperty(nameof(BaseEntity.Id));
            Assert.NotNull(idProperty);
            Assert.Equal(ValueGenerated.Never, idProperty!.ValueGenerated);
        }
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
