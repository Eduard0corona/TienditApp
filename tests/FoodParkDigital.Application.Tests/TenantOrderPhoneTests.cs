using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

/// <summary>An order for a business by its id (customer account, staff) answers a mistyped phone in Spanish.</summary>
public class TenantOrderPhoneTests
{
    [Theory]
    [InlineData("12")]
    [InlineData("no tengo")]
    public async Task InvalidPhone_ShouldBeRejectedWithASpanishMessage_BeforeLookingUpTheCustomer(string phone)
    {
        var tenantService = new FixedTenantService();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new ApplicationDbContext(options, tenantService, new NoOpMediator());
        var tenant = new Tenant("Taquería", "taqueria", BusinessType.Food, "dueno@taqueria.test");
        context.Tenants.Add(tenant);
        context.TenantSettings.Add(new TenantSettings(tenant.Id, TenantCapability.Food));
        await context.SaveChangesAsync();
        var lookup = new RecordingLookup();
        var handler = new CreateTenantOrderCommandHandler(
            context, new AnonymousUser(), null!, lookup, new FixedClock(), new PhoneNormalizationService());

        var result = await handler.Handle(new CreateTenantOrderCommand
        {
            TenantId = tenant.Id,
            Phone = phone,
            Items = [new CreateOrderItemDto { ProductId = Guid.NewGuid(), Quantity = 1 }]
        }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(PhoneNumberRules.InvalidPhoneMessage, result.Error);
        Assert.False(lookup.Called);
    }

    private sealed class RecordingLookup : ICustomerLookupService
    {
        public bool Called { get; private set; }

        public Task<CustomerLookupResult> ResolveForOrderAsync(Guid tenantId, string rawPhone, string? name, string defaultCountry,
            CancellationToken cancellationToken)
        {
            Called = true;
            throw new InvalidOperationException("The phone must be checked first.");
        }
    }

    private sealed class AnonymousUser : ICurrentUserService
    {
        public string? UserId => null;
        public string? UserName => null;
        public string? Role => null;
        public string? TenantRole => null;
        public IReadOnlyCollection<string> Permissions => [];
        public Guid? TenantId => null;
    }

    private sealed class FixedClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => new(2026, 10, 4, 18, 0, 0, TimeSpan.Zero);
    }

    private sealed class FixedTenantService : ICurrentTenantService
    {
        public Guid TenantId { get; private set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    private sealed class NoOpMediator : IMediator
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default) => Task.FromResult<TResponse>(default!);
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => Task.CompletedTask;
        public Task<object?> Send(object request, CancellationToken ct = default) => Task.FromResult<object?>(null);
        public Task Publish(object notification, CancellationToken ct = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken ct = default) where TNotification : INotification => Task.CompletedTask;
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => AsyncEnumerable.Empty<TResponse>();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => AsyncEnumerable.Empty<object?>();
    }
}
