using System.Net;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>Background senders retry only what the provider may accept later, and keep its message id.</summary>
public class WhatsAppServiceResultTests
{
    [Fact]
    public async Task AcceptedMessage_ShouldKeepTheProviderMessageId()
    {
        var http = new StubHttpMessageHandler(HttpStatusCode.OK,
            """{"messaging_product":"whatsapp","contacts":[{"input":"5215511112222","wa_id":"5215511112222"}],"messages":[{"id":"wamid.HBgM123"}]}""");
        var (service, tenantId, _) = await CreateAsync(http);

        var result = await service.SendTextMessageAsync(tenantId, "+5215511112222", "Hola");

        Assert.True(result.IsSuccess);
        Assert.Equal("wamid.HBgM123", result.ProviderMessageId);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    public async Task RejectedMessage_ShouldBeTransientOnlyWhenTheProviderMayAcceptItLater(HttpStatusCode status, bool transient)
    {
        var http = new StubHttpMessageHandler(status, """{"error":{"code":131000}}""");
        var (service, tenantId, _) = await CreateAsync(http);

        var result = await service.SendTemplateMessageAsync(tenantId, "+5215511112222", "order_status_update", "es_MX", ["ORD-1", "Lista"]);

        Assert.True(result.IsFailure);
        Assert.Equal(transient, result.IsTransient);
    }

    [Fact]
    public async Task BusinessWithoutWhatsApp_ShouldNotBeRetried()
    {
        await using var context = InMemoryApplicationDbContext.Create();
        var tenantId = Guid.NewGuid();
        var service = new WhatsAppService(
            context, Scope(tenantId), Client(new StubHttpMessageHandler(HttpStatusCode.OK)), NullLogger<WhatsAppService>.Instance);

        var result = await service.SendTextMessageAsync(tenantId, "+5215511112222", "Hola");

        Assert.True(result.IsFailure);
        Assert.False(result.IsTransient);
        Assert.Equal(WhatsAppService.NotConfiguredError, result.Error);
    }

    [Fact]
    public async Task AnotherBusinessInScope_ShouldNotSendWithThatBusinessesWhatsApp()
    {
        var http = new StubHttpMessageHandler(HttpStatusCode.OK);
        var (_, otherBusiness, context) = await CreateAsync(http);
        // A request or worker of one business asks to send with another's WhatsApp: the token is not used.
        var service = new WhatsAppService(context, Scope(Guid.NewGuid()), Client(http), NullLogger<WhatsAppService>.Instance);

        var result = await service.SendTextMessageAsync(otherBusiness, "+5215511112222", "Hola");

        Assert.Equal(WhatsAppService.NotConfiguredError, result.Error);
        Assert.Empty(http.Requests);
    }

    private static async Task<(WhatsAppService Service, Guid TenantId, ApplicationDbContext Context)> CreateAsync(StubHttpMessageHandler http)
    {
        var context = InMemoryApplicationDbContext.Create();
        var tenantId = Guid.NewGuid();
        var config = new WhatsAppConfig(tenantId);
        config.Update(isEnabled: true, accessToken: "token", phoneNumberId: "2233445566", wabaId: null, displayPhoneNumber: null);
        context.WhatsAppConfigs.Add(config);
        await context.SaveChangesAsync();
        return (new WhatsAppService(context, Scope(tenantId), Client(http), NullLogger<WhatsAppService>.Instance), tenantId, context);
    }

    private static WhatsAppCloudClient Client(StubHttpMessageHandler http)
        => new(http, new TestHostEnvironment(Environments.Production), NullLogger<WhatsAppCloudClient>.Instance);

    /// <summary>The business the service runs for, as a request or a worker scope sets it.</summary>
    private static ICurrentTenantService Scope(Guid tenantId)
    {
        var scope = new InMemoryApplicationDbContext.TestCurrentTenantService();
        scope.SetTenant(tenantId);
        return scope;
    }
}
