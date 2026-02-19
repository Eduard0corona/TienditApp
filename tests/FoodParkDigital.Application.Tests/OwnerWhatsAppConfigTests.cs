using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Identity.Commands;
using FoodParkDigital.Application.Identity.Queries;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

public class OwnerWhatsAppConfigTests
{
    [Fact]
    public async Task UpdateOwnerWhatsAppConfig_ShouldCreateConfig_ForTenantAdmin()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options, new TestCurrentTenantService(), new NullMediator());
        var userId = Guid.NewGuid();

        context.Users.Add(new ApplicationUser
        {
            Id = userId,
            UserName = "owner@example.com",
            Email = "owner@example.com",
            FirstName = "Owner",
            LastName = "User",
            Role = UserRole.TenantAdmin
        });
        await context.SaveChangesAsync(CancellationToken.None);

        var currentUser = new TestCurrentUserService(userId.ToString(), "TENANT_ADMIN");
        var handler = new UpdateOwnerWhatsAppConfigCommandHandler(context, currentUser);

        var result = await handler.Handle(new UpdateOwnerWhatsAppConfigCommand
        {
            IsEnabled = true,
            UseForOtp = true,
            AccessToken = "token-123",
            PhoneNumberId = "123456789",
            WabaId = "987654321",
            DisplayPhoneNumber = "+52 55 1111 2222",
            OtpTemplateName = "otp_code",
            OtpTemplateLanguageCode = "es_MX"
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.IsEnabled);
        Assert.True(result.Value.UseForOtp);
        Assert.True(result.Value.HasAccessToken);

        var config = await context.OwnerWhatsAppConfigs.SingleAsync();
        Assert.Equal(userId, config.UserId);
        Assert.Equal("otp_code", config.OtpTemplateName);
    }

    [Fact]
    public async Task GetOwnerWhatsAppConfig_ShouldReturnDefaults_WhenConfigDoesNotExist()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options, new TestCurrentTenantService(), new NullMediator());
        var userId = Guid.NewGuid();

        context.Users.Add(new ApplicationUser
        {
            Id = userId,
            UserName = "owner2@example.com",
            Email = "owner2@example.com",
            FirstName = "Owner",
            LastName = "User",
            Role = UserRole.TenantAdmin
        });
        await context.SaveChangesAsync(CancellationToken.None);

        var currentUser = new TestCurrentUserService(userId.ToString(), "TENANT_ADMIN");
        var handler = new GetOwnerWhatsAppConfigQueryHandler(context, currentUser);

        var result = await handler.Handle(new GetOwnerWhatsAppConfigQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(userId, result.Value.UserId);
        Assert.False(result.Value.IsEnabled);
        Assert.True(result.Value.UseForOtp);
        Assert.False(result.Value.HasAccessToken);
    }

    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public TestCurrentUserService(string userId, string role)
        {
            UserId = userId;
            Role = role;
        }

        public string? UserId { get; }
        public string? UserName => "test@example.com";
        public string? Role { get; }
        public string? TenantRole => null;
        public IReadOnlyCollection<string> Permissions => [];
        public Guid? TenantId => null;
    }

    private sealed class TestCurrentTenantService : ICurrentTenantService
    {
        public Guid TenantId { get; private set; } = Guid.Empty;
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
