using System.Net;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
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

    [Fact]
    public async Task Document_ShouldBeUploaded_ThenGoInTheHeaderOfTheTemplate_WithItsBodyParameters()
    {
        var http = new StubHttpMessageHandler(HttpStatusCode.OK, """{"id":"media-1","messages":[{"id":"wamid.HBgM9"}]}""");
        var (service, tenantId, _) = await CreateAsync(http);

        var result = await service.SendDocumentAsync(
            tenantId, "+5215511112222", new WhatsAppDocument("%PDF-1.7"u8.ToArray(), "application/pdf", "F-1_uuid.pdf"),
            "Hola", "factura_pdf", "es_MX", ["Negocio demo", "https://tienditapp.mx/factura#abc"]);

        Assert.True(result.IsSuccess);
        Assert.Equal("wamid.HBgM9", result.ProviderMessageId);
        Assert.Equal(2, http.Requests.Count);
        // Meta takes the PDF uploaded for the business's number first.
        Assert.EndsWith("/2233445566/media", http.Requests[0].Uri.AbsolutePath);
        Assert.Contains("name=messaging_product", http.Requests[0].Content);
        Assert.Contains("filename=F-1_uuid.pdf", http.Requests[0].Content);
        Assert.Contains("%PDF-1.7", http.Requests[0].Content);
        // Then the template, with the uploaded document in its header and the parameters in its body.
        Assert.EndsWith("/2233445566/messages", http.Requests[1].Uri.AbsolutePath);
        using var payload = System.Text.Json.JsonDocument.Parse(http.Requests[1].Content);
        var template = payload.RootElement.GetProperty("template");
        Assert.Equal("factura_pdf", template.GetProperty("name").GetString());
        var header = template.GetProperty("components")[0];
        Assert.Equal("header", header.GetProperty("type").GetString());
        var document = header.GetProperty("parameters")[0].GetProperty("document");
        Assert.Equal(("media-1", "F-1_uuid.pdf"), (document.GetProperty("id").GetString(), document.GetProperty("filename").GetString()));
        var body = template.GetProperty("components")[1];
        Assert.Equal(["Negocio demo", "https://tienditapp.mx/factura#abc"],
            body.GetProperty("parameters").EnumerateArray().Select(p => p.GetProperty("text").GetString()));
    }

    [Fact]
    public async Task Document_WithoutATemplate_ShouldGoWithItsCaption()
    {
        var http = new StubHttpMessageHandler(HttpStatusCode.OK, """{"id":"media-2","messages":[{"id":"wamid.HBgM10"}]}""");
        var (service, tenantId, _) = await CreateAsync(http);

        var result = await service.SendDocumentAsync(
            tenantId, "+5215511112222", new WhatsAppDocument("%PDF-1.7"u8.ToArray(), "application/pdf", "F-1_uuid.pdf"),
            "Hola: tu factura. " + new string('x', 2000), null, "es_MX", []);

        Assert.True(result.IsSuccess);
        using var payload = System.Text.Json.JsonDocument.Parse(http.Requests[1].Content);
        Assert.Equal("document", payload.RootElement.GetProperty("type").GetString());
        var document = payload.RootElement.GetProperty("document");
        Assert.Equal(("media-2", "F-1_uuid.pdf"), (document.GetProperty("id").GetString(), document.GetProperty("filename").GetString()));
        // Meta takes captions of up to 1,024 characters.
        Assert.Equal(WhatsAppCloudClient.MaxCaptionLength, document.GetProperty("caption").GetString()!.Length);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    public async Task FailedUpload_ShouldSendNoMessage_AndBeRetriedOnlyWhenMetaMayTakeItLater(HttpStatusCode status, bool transient)
    {
        var http = new StubHttpMessageHandler(status, """{"error":{"code":131053}}""");
        var (service, tenantId, _) = await CreateAsync(http);

        var result = await service.SendDocumentAsync(
            tenantId, "+5215511112222", new WhatsAppDocument("%PDF"u8.ToArray(), "application/pdf", "F-1.pdf"), "Hola", null, "es_MX", []);

        Assert.True(result.IsFailure);
        Assert.Equal(transient, result.IsTransient);
        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task Document_ForAnotherBusiness_ShouldNotUseThatBusinessesWhatsApp()
    {
        var http = new StubHttpMessageHandler(HttpStatusCode.OK);
        var (_, otherBusiness, context) = await CreateAsync(http);
        var service = new WhatsAppService(context, Scope(Guid.NewGuid()), Client(http), NullLogger<WhatsAppService>.Instance);

        var result = await service.SendDocumentAsync(
            otherBusiness, "+5215511112222", new WhatsAppDocument("%PDF"u8.ToArray(), "application/pdf", "F-1.pdf"), "Hola", null, "es_MX", []);

        Assert.Equal(WhatsAppService.NotConfiguredError, result.Error);
        Assert.Empty(http.Requests);
    }

    private static async Task<(WhatsAppService Service, Guid TenantId, ApplicationDbContext Context)> CreateAsync(StubHttpMessageHandler http)
    {
        var tenantId = Guid.NewGuid();
        var scope = Scope(tenantId);
        var context = InMemoryApplicationDbContext.Create(scope);
        var config = new WhatsAppConfig(tenantId);
        config.Update(isEnabled: true, accessToken: "token", phoneNumberId: "2233445566", wabaId: null, displayPhoneNumber: null);
        context.WhatsAppConfigs.Add(config);
        await context.SaveChangesAsync();
        return (new WhatsAppService(context, scope, Client(http), NullLogger<WhatsAppService>.Instance), tenantId, context);
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
