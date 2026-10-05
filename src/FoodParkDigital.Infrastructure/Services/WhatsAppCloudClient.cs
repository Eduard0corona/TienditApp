using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FoodParkDigital.Application.Common.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Infrastructure.Services;

/// <summary>The number that sends and its token: a business's WhatsApp, or the platform's for verification codes.</summary>
public sealed record WhatsAppSenderAccount(string PhoneNumberId, string AccessToken);

/// <summary>
/// The only client of the WhatsApp Cloud API (Meta Graph). The messages of each business
/// (<see cref="WhatsAppService"/>) and the platform's verification codes (<see cref="SmsService"/>) go through it,
/// so the API version, the payloads, which failures are worth retrying and what reaches the logs live in one place.
/// Meta's error bodies can echo the recipient or the text: outside Development only the status and Meta's error
/// code are logged, and the recipient is masked.
/// </summary>
public sealed class WhatsAppCloudClient
{
    public const string HttpClientName = "WhatsApp";
    private const string GraphApiBaseUrl = "https://graph.facebook.com/v21.0";

    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WhatsAppCloudClient> _logger;
    private readonly bool _logSensitiveData;

    public WhatsAppCloudClient(
        IHttpClientFactory httpClientFactory,
        IHostEnvironment environment,
        ILogger<WhatsAppCloudClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _logSensitiveData = environment.IsDevelopment();
    }

    /// <summary>Free text: WhatsApp only delivers it within 24 hours of the customer's last message.</summary>
    public Task<MessageSendResult> SendTextAsync(
        WhatsAppSenderAccount account,
        string to,
        string text,
        CancellationToken cancellationToken)
        => SendAsync(account, to, new
        {
            messaging_product = "whatsapp",
            to,
            type = "text",
            text = new { body = text }
        }, cancellationToken);

    /// <summary>A template approved in Meta, with its body parameters in order (<c>{{1}}</c>, <c>{{2}}</c>…).</summary>
    public Task<MessageSendResult> SendTemplateAsync(
        WhatsAppSenderAccount account,
        string to,
        string templateName,
        string languageCode,
        IReadOnlyList<string> parameters,
        CancellationToken cancellationToken)
        => SendAsync(account, to, new
        {
            messaging_product = "whatsapp",
            to,
            type = "template",
            template = new
            {
                name = templateName,
                language = new { code = languageCode },
                components = parameters.Count > 0
                    ? new[]
                    {
                        new
                        {
                            type = "body",
                            parameters = parameters.Select(p => new { type = "text", text = p }).ToArray()
                        }
                    }
                    : Array.Empty<object>()
            }
        }, cancellationToken);

    private async Task<MessageSendResult> SendAsync(
        WhatsAppSenderAccount account,
        string to,
        object payload,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{GraphApiBaseUrl}/{account.PhoneNumberId}/messages");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
            request.Content = JsonContent.Create(payload, options: PayloadOptions);

            using var response = await client.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "WhatsApp message sent to {Recipient} from {PhoneNumberId}",
                    LoggablePhone(to),
                    account.PhoneNumberId);
                return MessageSendResult.Sent(GetMessageId(responseBody));
            }

            _logger.LogError(
                "WhatsApp message to {Recipient} was not accepted. Status: {StatusCode}. Provider error code: {ProviderErrorCode}",
                LoggablePhone(to),
                response.StatusCode,
                LogRedaction.GetProviderErrorCode(responseBody));
            if (_logSensitiveData)
                _logger.LogInformation("WhatsApp response body: {Body}", responseBody);
            return MessageSendResult.Failed(
                $"Error al enviar mensaje de WhatsApp: {response.StatusCode}",
                isTransient: IsTransientStatus((int)response.StatusCode));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // No connection, DNS or a timeout: the message may be accepted on a later attempt.
            _logger.LogError(ex, "WhatsApp message to {Recipient} could not reach the API", LoggablePhone(to));
            return MessageSendResult.Failed("Error de conexión con la API de WhatsApp.", isTransient: true);
        }
    }

    private string LoggablePhone(string phoneNumber)
        => _logSensitiveData ? phoneNumber : LogRedaction.MaskPhoneNumber(phoneNumber);

    /// <summary>Throttling, timeouts and server errors can succeed later; other 4xx responses will not.</summary>
    internal static bool IsTransientStatus(int statusCode)
        => statusCode is 408 or 429 || statusCode >= 500;

    /// <summary>Reads the <c>wamid</c> of <c>{"messages": [{"id": "wamid..."}]}</c>.</summary>
    internal static string? GetMessageId(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
            return null;

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("messages", out var messages)
                && messages.ValueKind == JsonValueKind.Array
                && messages.GetArrayLength() > 0
                && messages[0].TryGetProperty("id", out var id)
                && id.ValueKind == JsonValueKind.String
                    ? id.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
