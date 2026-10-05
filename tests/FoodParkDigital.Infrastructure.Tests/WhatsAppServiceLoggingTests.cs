using System.Net;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Infrastructure.Tests;

public class WhatsAppServiceLoggingTests
{
    private const string PhoneNumber = "+525512345678";
    private const string NationalNumber = "5512345678";
    private const string Message = "Hola Ana, tu pedido ORD-000123 ya está listo";
    private const string AccessToken = "tenant-whatsapp-access-token";

    private const string MetaRejectedMessageResponse = """
        {"error": {"message": "(#131008) Required parameter is missing", "type": "OAuthException", "code": 131008, "error_data": {"messaging_product": "whatsapp", "details": "Recipient +525512345678, text: Hola Ana, tu pedido ORD-000123 ya está listo"}, "fbtrace_id": "AbC123"}}
        """;

    [Fact]
    public async Task SendTextMessageAsync_WhenMetaRejectsMessage_LogsStatusAndProviderCodeOutsideDevelopment()
    {
        var http = new StubHttpMessageHandler(HttpStatusCode.BadRequest, MetaRejectedMessageResponse);
        var tenantId = Guid.NewGuid();
        await using var context = InMemoryApplicationDbContext.Create();
        await SeedWhatsAppConfigAsync(context, tenantId);
        var logger = new CapturingLogger<WhatsAppService>();
        var clientLogger = new CapturingLogger<WhatsAppCloudClient>();
        var service = new WhatsAppService(context, Scope(tenantId), Client(http, Environments.Production, clientLogger), logger);

        var result = await service.SendTextMessageAsync(tenantId, PhoneNumber, Message);

        Assert.True(result.IsFailure);
        Assert.Single(http.Requests);
        foreach (var text in new[] { NationalNumber, "ORD-000123", AccessToken })
        {
            Assert.Null(logger.FirstMentioning(text));
            Assert.Null(clientLogger.FirstMentioning(text));
        }

        var error = Assert.Single(clientLogger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Equal(HttpStatusCode.BadRequest, error["StatusCode"]);
        Assert.Equal(131008, error["ProviderErrorCode"]);
        Assert.Contains("***5678", error.Message);
    }

    [Fact]
    public async Task SendTextMessageAsync_WhenMetaRejectsMessageInDevelopment_LogsProviderResponseForDebugging()
    {
        var http = new StubHttpMessageHandler(HttpStatusCode.BadRequest, MetaRejectedMessageResponse);
        var tenantId = Guid.NewGuid();
        await using var context = InMemoryApplicationDbContext.Create();
        await SeedWhatsAppConfigAsync(context, tenantId);
        var clientLogger = new CapturingLogger<WhatsAppCloudClient>();
        var service = new WhatsAppService(
            context, Scope(tenantId), Client(http, Environments.Development, clientLogger), new CapturingLogger<WhatsAppService>());

        await service.SendTextMessageAsync(tenantId, PhoneNumber, Message);

        Assert.Contains(clientLogger.Entries, entry => entry.Message.Contains("ORD-000123"));
    }

    private static WhatsAppCloudClient Client(StubHttpMessageHandler http, string environmentName, CapturingLogger<WhatsAppCloudClient> logger)
        => new(http, new TestHostEnvironment(environmentName), logger);

    private static ICurrentTenantService Scope(Guid tenantId)
    {
        var scope = new InMemoryApplicationDbContext.TestCurrentTenantService();
        scope.SetTenant(tenantId);
        return scope;
    }

    private static async Task SeedWhatsAppConfigAsync(ApplicationDbContext context, Guid tenantId)
    {
        var config = new WhatsAppConfig(tenantId);
        config.Update(
            isEnabled: true,
            accessToken: AccessToken,
            phoneNumberId: "2233445566",
            wabaId: null,
            displayPhoneNumber: null);
        context.WhatsAppConfigs.Add(config);
        await context.SaveChangesAsync();
    }
}
