using System.Net;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

public class SmsServiceTests
{
    [Fact]
    public async Task SendVerificationCode_ShouldNotUseTenantOwnerWhatsAppAccount()
    {
        await using var context = CreateContext();
        await AddOwnerConfigAsync(context, UserRole.TenantAdmin, "tenant-phone-id");
        var http = new RecordingHttpClientFactory();
        var service = CreateService(context, http);

        await service.SendVerificationCodeAsync("+525511112222", "123456");

        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task SendVerificationCode_ShouldUseSuperAdminWhatsAppAccount()
    {
        await using var context = CreateContext();
        await AddOwnerConfigAsync(context, UserRole.TenantAdmin, "tenant-phone-id");
        await AddOwnerConfigAsync(context, UserRole.SuperAdmin, "platform-phone-id");
        var http = new RecordingHttpClientFactory();
        var service = CreateService(context, http);

        await service.SendVerificationCodeAsync("+525511112222", "123456");

        var request = Assert.Single(http.Requests);
        Assert.Contains("/platform-phone-id/messages", request.RequestUri!.AbsoluteUri);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, new TestCurrentTenantService(), new NullMediator());
    }

    private static async Task AddOwnerConfigAsync(ApplicationDbContext context, UserRole role, string phoneNumberId)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = $"{phoneNumberId}@example.com",
            Email = $"{phoneNumberId}@example.com",
            FirstName = "Owner",
            LastName = "User",
            Role = role,
            IsActive = true
        };
        context.Users.Add(user);

        var config = new OwnerWhatsAppConfig(user.Id);
        config.Update(true, true, "token", phoneNumberId, "waba", "+5215500000000", "otp_code", "es_MX");
        context.OwnerWhatsAppConfigs.Add(config);
        await context.SaveChangesAsync();
    }

    private static SmsService CreateService(ApplicationDbContext context, IHttpClientFactory httpClientFactory) =>
        new(
            context,
            new ConfigurationBuilder().Build(),
            httpClientFactory,
            new WhatsAppCloudClient(httpClientFactory, new TestHostEnvironment(), NullLogger<WhatsAppCloudClient>.Instance),
            new TestHostEnvironment(),
            NullLogger<SmsService>.Instance);

    private sealed class RecordingHttpClientFactory : IHttpClientFactory
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public HttpClient CreateClient(string name) => new(new RecordingHandler(Requests));

        private sealed class RecordingHandler(List<HttpRequestMessage> requests) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                requests.Add(request);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
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
