using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.Identity.Commands;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace FoodParkDigital.Application.Tests;

public class ConfirmPhoneCommandTests
{
    [Fact]
    public async Task ConfirmPhone_ShouldCreateTenantMembership_WhenBusinessUserHasDefaultTenantWithoutMembership()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
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
