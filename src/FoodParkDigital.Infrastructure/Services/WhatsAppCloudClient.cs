using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Infrastructure.Services;

/// <summary>The number that sends and its token: a business's WhatsApp, or the platform's for verification codes.</summary>
public sealed record WhatsAppSenderAccount(string PhoneNumberId, string AccessToken);

/// <summary>A document uploaded to Meta (its media id) and the name the customer sees.</summary>
public sealed record WhatsAppMediaDocument(string MediaId, string FileName);

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
    /// <summary>The longest caption Meta takes for a document.</summary>
    public const int MaxCaptionLength = 1024;

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

    /// <summary>
    /// A template approved in Meta, with its body parameters in order (<c>{{1}}</c>, <c>{{2}}</c>…) and, when its header is a
    /// document, the uploaded one it carries (Fase 28M).
    /// </summary>
    public Task<MessageSendResult> SendTemplateAsync(
        WhatsAppSenderAccount account,
        string to,
        string templateName,
        string languageCode,
        IReadOnlyList<string> parameters,
        CancellationToken cancellationToken,
        WhatsAppMediaDocument? headerDocument = null)
    {
        var components = new List<object>();
        if (headerDocument is not null)
            components.Add(new
            {
                type = "header",
                parameters = new[] { new { type = "document", document = new { id = headerDocument.MediaId, filename = headerDocument.FileName } } }
            });
        if (parameters.Count > 0)
            components.Add(new { type = "body", parameters = parameters.Select(p => new { type = "text", text = p }).ToArray() });

        return SendAsync(account, to, new
        {
            messaging_product = "whatsapp",
            to,
            type = "template",
            template = new { name = templateName, language = new { code = languageCode }, components }
        }, cancellationToken);
    }

    /// <summary>An uploaded document with its caption: like free text, only delivered within 24 hours (Fase 28M).</summary>
    public Task<MessageSendResult> SendDocumentAsync(
        WhatsAppSenderAccount account,
        string to,
        WhatsAppMediaDocument document,
        string caption,
        CancellationToken cancellationToken)
        => SendAsync(account, to, new
        {
            messaging_product = "whatsapp",
            to,
            type = "document",
            document = new
            {
                id = document.MediaId,
                filename = document.FileName,
                caption = caption.Length <= MaxCaptionLength ? caption : caption[..MaxCaptionLength]
            }
        }, cancellationToken);

    /// <summary>
    /// Uploads a file for the business's number (<c>POST /{phone-number-id}/media</c>, Fase 28M): its media id, which a message
    /// carries within 30 days, as <see cref="MessageSendResult.ProviderMessageId"/>. Only the status and Meta's error code
    /// reach the logs, never the file.
    /// </summary>
    public async Task<MessageSendResult> UploadMediaAsync(
        WhatsAppSenderAccount account,
        WhatsAppDocument document,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{GraphApiBaseUrl}/{account.PhoneNumberId}/media");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
            var file = new ByteArrayContent(document.Content);
            file.Headers.ContentType = new MediaTypeHeaderValue(document.ContentType);
            request.Content = new MultipartFormDataContent
            {
                { new StringContent("whatsapp"), "messaging_product" },
                { new StringContent(document.ContentType), "type" },
                { file, "file", document.FileName }
            };

            using var response = await client.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode && GetMediaId(responseBody) is { } mediaId)
                return MessageSendResult.Sent(mediaId);

            _logger.LogError(
                "WhatsApp media upload from {PhoneNumberId} was not accepted. Status: {StatusCode}. Provider error code: {ProviderErrorCode}",
                account.PhoneNumberId,
                response.StatusCode,
                LogRedaction.GetProviderErrorCode(responseBody));
            return MessageSendResult.Failed(
                $"WhatsApp no recibió el documento: {response.StatusCode}",
                // Accepted without its media id: Meta may answer it whole on the next try.
                isTransient: response.IsSuccessStatusCode || IsTransientStatus((int)response.StatusCode));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError("WhatsApp media upload from {PhoneNumberId} could not reach the API: {ErrorType}", account.PhoneNumberId, ex.GetType().Name);
            return MessageSendResult.Failed("Error de conexión con la API de WhatsApp.", isTransient: true);
        }
    }

    /// <summary>
    /// Downloads a file a customer sent to the business's number (Fase 31L, ADR 0092): first its address
    /// (<c>GET /{media-id}</c>), then its content from Meta's servers, both with the business's token, never bigger than
    /// <paramref name="maxBytes"/>. The token only goes to Meta's own hosts over https. What Meta refuses or no longer has
    /// is <see cref="WhatsAppMediaDownloadOutcome.Unavailable"/>; a timeout, throttling or a failure of theirs,
    /// <see cref="WhatsAppMediaDownloadOutcome.Transient"/>. Only the status and Meta's error code reach the logs, never the
    /// file or its address.
    /// </summary>
    public async Task<WhatsAppMediaDownload> DownloadMediaAsync(
        WhatsAppSenderAccount account,
        string mediaId,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            Uri address;
            using (var request = new HttpRequestMessage(HttpMethod.Get, $"{GraphApiBaseUrl}/{Uri.EscapeDataString(mediaId)}"))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
                using var response = await client.SendAsync(request, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                    return MediaFailure("address", response.StatusCode, body);

                var (url, size) = ReadMediaAddress(body);
                if (size > maxBytes)
                    return WhatsAppMediaDownload.Unavailable;
                if (url is null || !IsMetaMediaHost(url))
                {
                    _logger.LogWarning("WhatsApp media of {PhoneNumberId} came without an address of Meta's servers", account.PhoneNumberId);
                    return WhatsAppMediaDownload.Unavailable;
                }
                address = url;
            }

            using var download = new HttpRequestMessage(HttpMethod.Get, address);
            download.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
            using var file = await client.SendAsync(download, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!file.IsSuccessStatusCode)
                return MediaFailure("file", file.StatusCode, null);
            if (file.Content.Headers.ContentLength > maxBytes)
                return WhatsAppMediaDownload.Unavailable;

            await using var stream = await file.Content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            {
                // Bigger than its size said: it is not kept, and the rest is not read.
                if (buffer.Length + read > maxBytes)
                    return WhatsAppMediaDownload.Unavailable;
                buffer.Write(chunk, 0, read);
            }

            return new WhatsAppMediaDownload(WhatsAppMediaDownloadOutcome.Downloaded, buffer.ToArray());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("WhatsApp media of {PhoneNumberId} could not be downloaded: {ErrorType}", account.PhoneNumberId, ex.GetType().Name);
            return WhatsAppMediaDownload.Transient;
        }
    }

    private WhatsAppMediaDownload MediaFailure(string step, System.Net.HttpStatusCode status, string? body)
    {
        _logger.LogWarning(
            "WhatsApp media {Step} was not given. Status: {StatusCode}. Provider error code: {ProviderErrorCode}",
            step,
            status,
            LogRedaction.GetProviderErrorCode(body));
        return IsTransientStatus((int)status) ? WhatsAppMediaDownload.Transient : WhatsAppMediaDownload.Unavailable;
    }

    /// <summary>Meta's servers of WhatsApp's files: only there goes the token.</summary>
    internal static bool IsMetaMediaHost(Uri url)
        => url.Scheme == Uri.UriSchemeHttps
            && url.IsDefaultPort
            && (url.Host.Equals("lookaside.fbsbx.com", StringComparison.OrdinalIgnoreCase)
                || url.Host.EndsWith(".fbsbx.com", StringComparison.OrdinalIgnoreCase)
                || url.Host.EndsWith(".fbcdn.net", StringComparison.OrdinalIgnoreCase));

    /// <summary>Reads <c>{"url": "https://…", "file_size": 1234}</c>.</summary>
    internal static (Uri? Url, long Size) ReadMediaAddress(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
            return (null, 0);

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return (null, 0);
            Uri? url = root.TryGetProperty("url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String
                && Uri.TryCreate(urlProp.GetString(), UriKind.Absolute, out var parsed)
                    ? parsed
                    : null;
            var size = root.TryGetProperty("file_size", out var sizeProp) && sizeProp.ValueKind == JsonValueKind.Number
                && sizeProp.TryGetInt64(out var bytes)
                    ? bytes
                    : 0;
            return (url, size);
        }
        catch (JsonException)
        {
            return (null, 0);
        }
    }

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

    /// <summary>Reads the media id of <c>{"id": "1234…"}</c>.</summary>
    internal static string? GetMediaId(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
            return null;

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("id", out var id)
                && id.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(id.GetString())
                    ? id.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

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
