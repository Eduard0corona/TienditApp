using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Customers.Enums;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

public class TemporaryCustomerClaimServiceTests
{
    [Fact]
    public async Task ClaimByPhoneAsync_ShouldLinkOrdersAndMarkTemporaryCustomerClaimed()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var tenantService = new TestCurrentTenantService();
        await using var context = new ApplicationDbContext(options, tenantService, new NullMediator());

        var tenant = new Tenant("Tenant Uno", "tenant-uno", BusinessType.Food, "tenant@demo.com");
        context.Tenants.Add(tenant);

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "customer@demo.com",
            NormalizedUserName = "CUSTOMER@DEMO.COM",
            Email = "customer@demo.com",
            NormalizedEmail = "CUSTOMER@DEMO.COM",
            FirstName = "Ana",
            LastName = "Cliente",
            Role = UserRole.Customer,
            IsActive = true
        };
        context.Users.Add(user);

        var temp = new TemporaryCustomer(tenant.Id, "+525512345678", "Cliente Temporal");
        context.TemporaryCustomers.Add(temp);

        var order = new Order(
            tenantId: tenant.Id,
            customerUserId: null,
            temporaryCustomerId: temp.Id,
            createdByBusiness: false,
            orderNumber: "ORD-000001",
            businessType: tenant.BusinessType,
            orderType: FoodParkDigital.Domain.Orders.Enums.OrderType.Retail,
            customerName: "Cliente Temporal",
            customerPhone: "+525512345678",
            customerNotes: null,
            tableNumber: null);
        typeof(Order).GetProperty("Version")!.SetValue(order, new byte[] { 1 });
        context.Orders.Add(order);

        await context.SaveChangesAsync();

        var service = new TemporaryCustomerClaimService(
            context,
            new PhoneNormalizationService(),
            new NullLogger<TemporaryCustomerClaimService>());

        var affected = await service.ClaimByPhoneAsync("55 1234 5678", user.Id, "MX", CancellationToken.None);

        Assert.Equal(1, affected);

        var reloadedTemp = await context.TemporaryCustomers.IgnoreQueryFilters().FirstAsync();
        var reloadedOrder = await context.Orders.IgnoreQueryFilters().FirstAsync();

        Assert.Equal(TemporaryCustomerStatus.Claimed, reloadedTemp.Status);
        Assert.Equal(user.Id, reloadedTemp.ClaimedUserId);
        Assert.Equal(user.Id, reloadedOrder.CustomerUserId);
        Assert.Null(reloadedOrder.TemporaryCustomerId);
    }

    private sealed class TestCurrentTenantService : ICurrentTenantService
    {
        public Guid TenantId { get; private set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
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
