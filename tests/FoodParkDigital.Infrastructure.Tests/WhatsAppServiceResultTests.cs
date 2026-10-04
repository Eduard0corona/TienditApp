using System.Net;
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
        var (service, tenantId) = await CreateAsync(http);

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
        var (service, tenantId) = await CreateAsync(http);

        var result = await service.SendTemplateMessageAsync(tenantId, "+5215511112222", "order_status_update", "es_MX", ["ORD-1", "Lista"]);

        Assert.True(result.IsFailure);
        Assert.Equal(transient, result.IsTransient);
    }

    [Fact]
    public async Task BusinessWithoutWhatsApp_ShouldNotBeRetried()
    {
        await using var context = InMemoryApplicationDbContext.Create();
        var service = new WhatsAppService(
            context, new StubHttpMessageHandler(HttpStatusCode.OK), new TestHostEnvironment(Environments.Production),
            NullLogger<WhatsAppService>.Instance);

        var result = await service.SendTextMessageAsync(Guid.NewGuid(), "+5215511112222", "Hola");

        Assert.True(result.IsFailure);
        Assert.False(result.IsTransient);
        Assert.Equal(WhatsAppService.NotConfiguredError, result.Error);
    }

    private static async Task<(WhatsAppService Service, Guid TenantId)> CreateAsync(StubHttpMessageHandler http)
    {
        var context = InMemoryApplicationDbContext.Create();
        var tenantId = Guid.NewGuid();
        var config = new WhatsAppConfig(tenantId);
        config.Update(isEnabled: true, accessToken: "token", phoneNumberId: "2233445566", wabaId: null, displayPhoneNumber: null);
        context.WhatsAppConfigs.Add(config);
        await context.SaveChangesAsync();
        return (new WhatsAppService(context, http, new TestHostEnvironment(Environments.Production), NullLogger<WhatsAppService>.Instance), tenantId);
    }
}
