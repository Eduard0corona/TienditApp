using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Infrastructure.Services;

public class WhatsAppService : IWhatsAppService
{
    private const string GraphApiBaseUrl = "https://graph.facebook.com/v21.0";

    private readonly IApplicationDbContext _context;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WhatsAppService> _logger;
    private readonly bool _logProviderResponses;

    public WhatsAppService(
        IApplicationDbContext context,
        IHttpClientFactory httpClientFactory,
        IHostEnvironment environment,
        ILogger<WhatsAppService> logger)
    {
        _context = context;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        // Meta error responses can echo recipients or message content: only Development logs them verbatim.
        _logProviderResponses = environment.IsDevelopment();
    }

    public async Task<Result> SendTemplateMessageAsync(
        Guid tenantId,
        string phoneNumber,
        string templateName,
        string languageCode,
        string[] parameters,
        CancellationToken cancellationToken = default)
    {
        var config = await GetConfigAsync(tenantId, cancellationToken);
        if (config is null)
            return Result.Failure("WhatsApp no está configurado para este tenant.");

        var body = new
        {
            messaging_product = "whatsapp",
            to = phoneNumber,
            type = "template",
            template = new
            {
                name = templateName,
                language = new { code = languageCode },
                components = parameters.Length > 0
                    ? new[]
                    {
                        new
                        {
                            type = "body",
                            parameters = parameters.Select(p => new
                            {
                                type = "text",
                                text = p
                            }).ToArray()
                        }
                    }
                    : Array.Empty<object>()
            }
        };

        return await SendRequestAsync(config.PhoneNumberId!, config.AccessToken!, body, cancellationToken);
    }

    public async Task<Result> SendTextMessageAsync(
        Guid tenantId,
        string phoneNumber,
        string message,
        CancellationToken cancellationToken = default)
    {
        var config = await GetConfigAsync(tenantId, cancellationToken);
        if (config is null)
            return Result.Failure("WhatsApp no está configurado para este tenant.");

        var body = new
        {
            messaging_product = "whatsapp",
            to = phoneNumber,
            type = "text",
            text = new { body = message }
        };

        return await SendRequestAsync(config.PhoneNumberId!, config.AccessToken!, body, cancellationToken);
    }

    private async Task<Domain.Tenants.Entities.WhatsAppConfig?> GetConfigAsync(
        Guid tenantId, CancellationToken cancellationToken)
    {
        var config = await _context.WhatsAppConfigs
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(w => w.TenantId == tenantId, cancellationToken);

        if (config is null || !config.IsEnabled)
        {
            _logger.LogWarning("WhatsApp not configured or disabled for tenant {TenantId}", tenantId);
            return null;
        }

        if (string.IsNullOrEmpty(config.AccessToken) || string.IsNullOrEmpty(config.PhoneNumberId))
        {
            _logger.LogWarning("WhatsApp credentials incomplete for tenant {TenantId}", tenantId);
            return null;
        }

        return config;
    }

    private async Task<Result> SendRequestAsync(
        string phoneNumberId, string accessToken, object body, CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("WhatsApp");
            var url = $"{GraphApiBaseUrl}/{phoneNumberId}/messages";

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = JsonContent.Create(body, options: new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

            var response = await client.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("WhatsApp message sent successfully to {PhoneNumberId}", phoneNumberId);
                return Result.Success();
            }

            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "WhatsApp API error: {StatusCode}. Provider error code: {ProviderErrorCode}",
                response.StatusCode,
                LogRedaction.GetProviderErrorCode(errorContent));
            if (_logProviderResponses)
                _logger.LogInformation("WhatsApp response body: {Body}", errorContent);
            return Result.Failure($"Error al enviar mensaje de WhatsApp: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send WhatsApp message");
            return Result.Failure("Error de conexión con la API de WhatsApp.");
        }
    }
}
