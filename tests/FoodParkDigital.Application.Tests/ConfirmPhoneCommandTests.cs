using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.Identity.Commands;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Customers.Enums;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace FoodParkDigital.Application.Tests;

public class ConfirmPhoneCommandTests
{
    private const string CustomerPhone = "+525512345678";

    [Fact]
    public async Task ConfirmPhone_ShouldCreateTenantMembership_WhenBusinessUserHasDefaultTenantWithoutMembership()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var tenantService = new TestCurrentTenantService();
        await using var context = new ApplicationDbContext(options, tenantService, new NullMediator());

        var tenant = new Tenant("Demo Tenant", "demo-tenant", BusinessType.Food, "demo@tenant.com");
        context.Tenants.Add(tenant);

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "owner@demo.com",
            NormalizedUserName = "OWNER@DEMO.COM",
            Email = "owner@demo.com",
            NormalizedEmail = "OWNER@DEMO.COM",
            FirstName = "Owner",
            LastName = "Demo",
            PhoneNumber = "+525511112222",
            PhoneNumberConfirmed = false,
            Role = UserRole.TenantAdmin,
            DefaultTenantId = tenant.Id,
            IsActive = true
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var userManager = new FakeUserManager(context);
        var claimService = new NoOpClaimService();
        var handler = new ConfirmPhoneCommandHandler(userManager, context, claimService);

        var result = await handler.Handle(
            new ConfirmPhoneCommand
            {
                UserId = user.Id.ToString(),
                Code = "123456"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var membership = await context.TenantUsers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.UserId == user.Id && x.TenantId == tenant.Id);

        Assert.NotNull(membership);
        Assert.Equal(TenantUserStatus.Active, membership!.Status);
        Assert.Equal(TenantUserRole.Admin, membership.Role);
    }

    [Fact]
    public async Task ConfirmPhone_ShouldConfirmAndClaimGuestOrders_WithRetryingExecutionStrategy()
    {
        await using var host = await SqliteTestHost.CreateAsync("CUSTOMER");
        var guest = await CustomerSeed.GuestOrderAsync(host, "tenant-uno", CustomerPhone);
        var customer = await CustomerSeed.UnconfirmedCustomerAsync(host, "cliente@demo.com", CustomerPhone);

        var result = await ConfirmAsync(host, customer);

        Assert.True(result.IsSuccess, result.Error);
        await AssertConfirmedAndClaimedAsync(host, customer.UserId, guest);
    }

    [Fact]
    public async Task ConfirmPhone_ShouldLeavePhoneUnconfirmed_WhenClaimingGuestOrdersFails()
    {
        await using var host = await SqliteTestHost.CreateAsync("CUSTOMER");
        var guest = await CustomerSeed.GuestOrderAsync(host, "tenant-uno", CustomerPhone);
        var customer = await CustomerSeed.UnconfirmedCustomerAsync(host, "cliente@demo.com", CustomerPhone);

        // Staff updates the guest order between the claim's read and its write: the rowversion no
        // longer matches and the claim fails after the phone confirmation was already written.
        host.BeforeSave.Arm(
            when: context => context.ChangeTracker.Entries<Order>().Any(e => e.State == EntityState.Modified),
            action: async context =>
            {
                var updated = await context.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE \"Orders\" SET \"Version\" = randomblob(8) WHERE \"Id\" = {guest.OrderId}");
                Assert.Equal(1, updated);
            });

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => ConfirmAsync(host, customer));

        Assert.True(host.BeforeSave.Fired);
        var afterFailure = await host.QueryAsync(async db => new
        {
            User = await db.Users.SingleAsync(x => x.Id == customer.UserId),
            Temp = await db.TemporaryCustomers.IgnoreQueryFilters().SingleAsync(x => x.Id == guest.TemporaryCustomerId)
        });
        Assert.False(afterFailure.User.PhoneNumberConfirmed);
        Assert.Equal(TemporaryCustomerStatus.PendingInvited, afterFailure.Temp.Status);

        // Nothing was left half-done, so retrying with the same code confirms and links.
        var retry = await ConfirmAsync(host, customer);

        Assert.True(retry.IsSuccess, retry.Error);
        await AssertConfirmedAndClaimedAsync(host, customer.UserId, guest);
    }

    [Fact]
    public async Task ConfirmPhone_ShouldRetryConfirmationAndClaim_WhenCommitFailsTransiently()
    {
        await using var host = await SqliteTestHost.CreateAsync("CUSTOMER");
        var guest = await CustomerSeed.GuestOrderAsync(host, "tenant-uno", CustomerPhone);
        var customer = await CustomerSeed.UnconfirmedCustomerAsync(host, "cliente@demo.com", CustomerPhone);
        host.CommitFaults.FailNext();

        // The retried attempt must verify the code against committed data, not the security stamp
        // the rolled-back attempt already rotated in memory.
        var result = await ConfirmAsync(host, customer);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(0, host.CommitFaults.Remaining);
        await AssertConfirmedAndClaimedAsync(host, customer.UserId, guest);
    }

    private static Task<Result> ConfirmAsync(SqliteTestHost host, UnconfirmedCustomer customer)
        => host.HandleAsync<ConfirmPhoneCommandHandler, Result>(handler => handler.Handle(
            new ConfirmPhoneCommand { UserId = customer.UserId.ToString(), Code = customer.PhoneCode },
            CancellationToken.None));

    private static async Task AssertConfirmedAndClaimedAsync(SqliteTestHost host, Guid userId, GuestOrder guest)
    {
        var stored = await host.QueryAsync(async db => new
        {
            User = await db.Users.SingleAsync(x => x.Id == userId),
            Temp = await db.TemporaryCustomers.IgnoreQueryFilters().SingleAsync(x => x.Id == guest.TemporaryCustomerId),
            Order = await db.Orders.IgnoreQueryFilters().SingleAsync(x => x.Id == guest.OrderId)
        });

        Assert.True(stored.User.PhoneNumberConfirmed);
        Assert.Equal(TemporaryCustomerStatus.Claimed, stored.Temp.Status);
        Assert.Equal(userId, stored.Temp.ClaimedUserId);
        Assert.Equal(userId, stored.Order.CustomerUserId);
        Assert.Null(stored.Order.TemporaryCustomerId);
    }

    private sealed class NoOpClaimService : ITemporaryCustomerClaimService
    {
        public Task<int> ClaimByPhoneAsync(string rawPhone, Guid userId, string defaultCountry, CancellationToken cancellationToken)
            => Task.FromResult(0);

        public Task<int> ClaimByPhoneForTenantAsync(string rawPhone, Guid userId, Guid tenantId, string defaultCountry, CancellationToken cancellationToken)
            => Task.FromResult(0);
    }

    private sealed class TestCurrentTenantService : ICurrentTenantService
    {
        public Guid TenantId { get; private set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    private sealed class FakeUserManager : UserManager<ApplicationUser>
    {
        public FakeUserManager(ApplicationDbContext context)
            : base(
                new UserStore<ApplicationUser, ApplicationRole, ApplicationDbContext, Guid>(context),
                Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
                new PasswordHasher<ApplicationUser>(),
                [],
                [],
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                new ServiceCollection().BuildServiceProvider(),
                new NullLogger<UserManager<ApplicationUser>>())
        {
        }

        public override Task<IdentityResult> ChangePhoneNumberAsync(ApplicationUser user, string phoneNumber, string token)
        {
            user.PhoneNumber = phoneNumber;
            user.PhoneNumberConfirmed = true;
            return Task.FromResult(IdentityResult.Success);
        }
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
