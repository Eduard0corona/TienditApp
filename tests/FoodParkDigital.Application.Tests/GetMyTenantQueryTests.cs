using AutoMapper;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Mappings;
using FoodParkDigital.Application.Tenants.Queries;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

public class GetMyTenantQueryTests
{
    [Fact]
    public async Task Handle_ShouldResolveTenantFromUserDefaultTenant_WhenTenantClaimIsMissing()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options, new TestCurrentTenantService(), new NullMediator());
        var mapper = CreateMapper();

        var tenant = new Tenant("Tenant Uno", "tenant-uno", BusinessType.Food, "tenant@demo.com");
        context.Tenants.Add(tenant);

        var otherTenant = new Tenant("Tenant Antiguo", "tenant-antiguo", BusinessType.Food, "old@demo.com");
        context.Tenants.Add(otherTenant);

        var user = BuildUser(tenant.Id);
        context.Users.Add(user);
        context.TenantUsers.Add(new TenantUser(otherTenant.Id, user.Id, TenantUserRole.Admin, TenantUserStatus.Active));
        context.TenantUsers.Add(new TenantUser(tenant.Id, user.Id, TenantUserRole.Admin, TenantUserStatus.Active));
        await context.SaveChangesAsync();

        var currentUser = new TestCurrentUserService(user.Id.ToString(), null);
        var handler = new GetMyTenantQueryHandler(context, currentUser, new TestCurrentTenantService(), mapper);

        var result = await handler.Handle(new GetMyTenantQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(tenant.Id, result.Value!.Id);
        Assert.Equal(tenant.Name, result.Value.Name);
    }

    [Fact]
    public async Task Handle_ShouldFail_WhenMembershipIsDisabled()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options, new TestCurrentTenantService(), new NullMediator());
        var tenant = new Tenant("Tenant Uno", "tenant-uno", BusinessType.Food, "tenant@demo.com");
        context.Tenants.Add(tenant);

        var user = BuildUser(tenant.Id);
        context.Users.Add(user);
        context.TenantUsers.Add(new TenantUser(tenant.Id, user.Id, TenantUserRole.Admin, TenantUserStatus.Disabled));
        await context.SaveChangesAsync();

        // Even with the tenant in the token claim and as default tenant, a disabled member gets nothing.
        var currentUser = new TestCurrentUserService(user.Id.ToString(), tenant.Id);
        var handler = new GetMyTenantQueryHandler(context, currentUser, new TestCurrentTenantService(), CreateMapper());

        var result = await handler.Handle(new GetMyTenantQuery(), CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Handle_ShouldUseTenantResolvedForRequest()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options, new TestCurrentTenantService(), new NullMediator());
        var claimTenant = new Tenant("Tenant Claim", "tenant-claim", BusinessType.Food, "claim@demo.com");
        var requestTenant = new Tenant("Tenant Request", "tenant-request", BusinessType.Retail, "request@demo.com");
        context.Tenants.AddRange(claimTenant, requestTenant);
        var user = BuildUser(claimTenant.Id);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var tenantService = new TestCurrentTenantService();
        tenantService.SetTenant(requestTenant.Id);
        var currentUser = new TestCurrentUserService(user.Id.ToString(), claimTenant.Id);
        var handler = new GetMyTenantQueryHandler(context, currentUser, tenantService, CreateMapper());

        var result = await handler.Handle(new GetMyTenantQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(requestTenant.Id, result.Value!.Id);
    }

    [Fact]
    public async Task Handle_ShouldResolveTenantFromMembership_WhenDefaultTenantIsMissing()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options, new TestCurrentTenantService(), new NullMediator());
        var mapper = CreateMapper();

        var tenant = new Tenant("Tenant Dos", "tenant-dos", BusinessType.Service, "tenant2@demo.com");
        context.Tenants.Add(tenant);

        var user = BuildUser(defaultTenantId: null);
        context.Users.Add(user);

        context.TenantUsers.Add(new TenantUser(tenant.Id, user.Id, TenantUserRole.Admin, TenantUserStatus.Active));
        await context.SaveChangesAsync();

        var currentUser = new TestCurrentUserService(user.Id.ToString(), null);
        var handler = new GetMyTenantQueryHandler(context, currentUser, new TestCurrentTenantService(), mapper);

        var result = await handler.Handle(new GetMyTenantQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(tenant.Id, result.Value!.Id);
    }

    private static IMapper CreateMapper()
    {
        var config = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>());
        return config.CreateMapper();
    }

    private static ApplicationUser BuildUser(Guid? defaultTenantId)
        => new()
        {
            Id = Guid.NewGuid(),
            UserName = "owner@demo.com",
            NormalizedUserName = "OWNER@DEMO.COM",
            Email = "owner@demo.com",
            NormalizedEmail = "OWNER@DEMO.COM",
            FirstName = "Owner",
            LastName = "Demo",
            Role = UserRole.TenantAdmin,
            DefaultTenantId = defaultTenantId,
            IsActive = true
        };

    private sealed class TestCurrentUserService(string? userId, Guid? tenantId) : ICurrentUserService
    {
        public string? UserId { get; } = userId;
        public string? UserName { get; } = null;
        public string? Role { get; } = null;
        public string? TenantRole { get; } = null;
        public IReadOnlyCollection<string> Permissions => [];
        public Guid? TenantId { get; } = tenantId;
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
