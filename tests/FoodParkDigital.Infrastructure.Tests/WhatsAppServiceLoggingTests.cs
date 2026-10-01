using System.Net;
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
        await using var context = InMemoryApplicationDbContext.Create();
        var tenantId = await SeedWhatsAppConfigAsync(context);
        var logger = new CapturingLogger<WhatsAppService>();
        var service = new WhatsAppService(context, http, new TestHostEnvironment(Environments.Production), logger);

        var result = await service.SendTextMessageAsync(tenantId, PhoneNumber, Message);

        Assert.True(result.IsFailure);
        Assert.Single(http.Requests);
        Assert.Null(logger.FirstMentioning(NationalNumber));
        Assert.Null(logger.FirstMentioning("ORD-000123"));
        Assert.Null(logger.FirstMentioning(AccessToken));
        var error = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Equal(HttpStatusCode.BadRequest, error["StatusCode"]);
        Assert.Equal(131008, error["ProviderErrorCode"]);
    }

    [Fact]
    public async Task SendTextMessageAsync_WhenMetaRejectsMessageInDevelopment_LogsProviderResponseForDebugging()
    {
        var http = new StubHttpMessageHandler(HttpStatusCode.BadRequest, MetaRejectedMessageResponse);
        await using var context = InMemoryApplicationDbContext.Create();
        var tenantId = await SeedWhatsAppConfigAsync(context);
        var logger = new CapturingLogger<WhatsAppService>();
        var service = new WhatsAppService(context, http, new TestHostEnvironment(Environments.Development), logger);

        await service.SendTextMessageAsync(tenantId, PhoneNumber, Message);

        Assert.Contains(logger.Entries, entry => entry.Message.Contains("ORD-000123"));
    }

    private static async Task<Guid> SeedWhatsAppConfigAsync(ApplicationDbContext context)
    {
        var tenantId = Guid.NewGuid();
        var config = new WhatsAppConfig(tenantId);
        config.Update(
            isEnabled: true,
            accessToken: AccessToken,
            phoneNumberId: "2233445566",
            wabaId: null,
            displayPhoneNumber: null);
        context.WhatsAppConfigs.Add(config);
        await context.SaveChangesAsync();
        return tenantId;
    }
}
