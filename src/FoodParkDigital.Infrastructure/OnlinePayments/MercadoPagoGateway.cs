using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.OnlinePayments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace FoodParkDigital.Infrastructure.OnlinePayments;

/// <summary>
/// The only client of Mercado Pago's API (Fase 24I, ADR 0031): OAuth with PKCE for a business's account, Checkout Pro
/// preferences, payments and refunds. Every payment call carries the business's key, so the money stays in its account.
/// The checkout takes only payments that are decided at once (cards and Mercado Pago's balance, <c>binary_mode</c>), in
/// one installment. Keys, codes and the payer never reach the logs: only the status and Mercado Pago's error code.
/// </summary>
public sealed partial class MercadoPagoGateway : IOnlinePaymentGateway
{
    public const string HttpClientName = "MercadoPago";

    /// <summary>
    /// Where Mercado Pago sends the owner back with the code (registered in the application): Configuración › Pagos en
    /// línea, which finishes the connection with the owner's session.
    /// </summary>
    public const string CallbackPath = "/settings/online-payments";

    private const string Currency = "MXN";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MercadoPagoOptions _options;
    private readonly IAppLinks _links;
    private readonly ISecretProtector _protector;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<MercadoPagoGateway> _logger;

    public MercadoPagoGateway(
        IHttpClientFactory httpClientFactory,
        IOptions<MercadoPagoOptions> options,
        IAppLinks links,
        ISecretProtector protector,
        IDateTimeProvider clock,
        ILogger<MercadoPagoGateway> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _links = links;
        _protector = protector;
        _clock = clock;
        _logger = logger;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.ClientId)
        && !string.IsNullOrWhiteSpace(_options.ClientSecret)
        && !string.IsNullOrWhiteSpace(_options.WebhookSecret)
        && _protector.IsConfigured
        && Uri.TryCreate(_links.BaseUrl, UriKind.Absolute, out var app)
        && (app.Scheme == Uri.UriSchemeHttps || app.Scheme == Uri.UriSchemeHttp);

    public bool Sandbox => _options.Sandbox;

    private string RedirectUri => $"{_links.BaseUrl}{CallbackPath}";

    public Uri AuthorizationUrl(Guid state, string codeChallenge)
    {
        EnsureConfigured();
        var query = string.Join('&',
            $"client_id={Uri.EscapeDataString(_options.ClientId!)}",
            "response_type=code",
            "platform_id=mp",
            $"state={state:N}",
            $"redirect_uri={Uri.EscapeDataString(RedirectUri)}",
            $"code_challenge={Uri.EscapeDataString(codeChallenge)}",
            "code_challenge_method=S256");
        return new Uri($"{_options.AuthorizationUrl}?{query}");
    }

    public Task<OnlinePaymentCredentials> ConnectAsync(string code, string codeVerifier, CancellationToken cancellationToken) =>
        TokenAsync(new
        {
            client_id = _options.ClientId,
            client_secret = _options.ClientSecret,
            grant_type = "authorization_code",
            code,
            redirect_uri = RedirectUri,
            code_verifier = codeVerifier,
            test_token = _options.Sandbox
        }, "connect", cancellationToken);

    public Task<OnlinePaymentCredentials> RenewAsync(string refreshToken, CancellationToken cancellationToken) =>
        TokenAsync(new
        {
            client_id = _options.ClientId,
            client_secret = _options.ClientSecret,
            grant_type = "refresh_token",
            refresh_token = refreshToken
        }, "renew", cancellationToken);

    public async Task<OnlinePaymentCheckoutPage> CreateCheckoutAsync(
        string accessToken, OnlinePaymentCheckoutRequest request, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var body = new
        {
            items = new[]
            {
                new { id = request.CheckoutId.ToString(), title = request.Title, quantity = 1, unit_price = request.Amount, currency_id = Currency }
            },
            external_reference = request.CheckoutId.ToString(),
            back_urls = new { success = request.ReturnUrl, failure = request.ReturnUrl, pending = request.ReturnUrl },
            auto_return = "approved",
            // Approved or rejected at once: no payment waits days (OXXO, transfers) for an order that is food or a visit.
            binary_mode = true,
            payment_methods = new
            {
                excluded_payment_types = new[] { new { id = "ticket" }, new { id = "atm" }, new { id = "bank_transfer" } },
                installments = 1
            },
            expires = true,
            expiration_date_from = MercadoPagoDate(_clock.UtcNow),
            expiration_date_to = MercadoPagoDate(request.ExpiresAt),
            statement_descriptor = StatementDescriptor(request.StatementDescriptor)
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{_options.ApiBaseUrl}/checkout/preferences")
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        Authorize(message, accessToken);
        message.Headers.Add("X-Idempotency-Key", request.CheckoutId.ToString());

        using var response = await SendAsync(message, "preference", cancellationToken);
        await ThrowIfKeyRejectedAsync(response, "preference", cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await UnavailableAsync(response, "preference", cancellationToken);

        using var json = await ReadJsonAsync(response, cancellationToken);
        var root = json.RootElement;
        var id = GetString(root, "id");
        var url = GetString(root, _options.Sandbox ? "sandbox_init_point" : "init_point") ?? GetString(root, "init_point");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(url))
            throw new OnlinePaymentGatewayUnavailableException("Mercado Pago no devolvió la página de pago.");
        return new OnlinePaymentCheckoutPage(id, url);
    }

    public async Task<OnlinePaymentDetails?> GetPaymentAsync(string accessToken, string paymentId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        // Mercado Pago's payment ids are numbers: anything else is not one, and never reaches the URL.
        if (!PaymentIdPattern().IsMatch(paymentId))
            return null;

        using var message = new HttpRequestMessage(HttpMethod.Get, $"{_options.ApiBaseUrl}/v1/payments/{paymentId}");
        Authorize(message, accessToken);
        using var response = await SendAsync(message, "payment", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        await ThrowIfKeyRejectedAsync(response, "payment", cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await UnavailableAsync(response, "payment", cancellationToken);

        using var json = await ReadJsonAsync(response, cancellationToken);
        return ParsePayment(json.RootElement);
    }

    public async Task<IReadOnlyList<OnlinePaymentDetails>> FindPaymentsAsync(
        string accessToken, Guid checkoutId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var message = new HttpRequestMessage(HttpMethod.Get,
            $"{_options.ApiBaseUrl}/v1/payments/search?external_reference={checkoutId}&sort=date_created&criteria=desc&limit=20");
        Authorize(message, accessToken);
        using var response = await SendAsync(message, "search", cancellationToken);
        await ThrowIfKeyRejectedAsync(response, "search", cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await UnavailableAsync(response, "search", cancellationToken);

        using var json = await ReadJsonAsync(response, cancellationToken);
        if (!json.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            return [];

        return results.EnumerateArray()
            .Select(ParsePayment)
            .Where(payment => payment is not null)
            .Select(payment => payment!)
            .ToList();
    }

    public async Task<OnlinePaymentRefundOutcome> RefundAsync(
        string accessToken, string paymentId, decimal amount, Guid idempotencyKey, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        if (!PaymentIdPattern().IsMatch(paymentId))
            return new OnlinePaymentRefundOutcome.Refused("El pago no tiene una referencia de Mercado Pago válida.");

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{_options.ApiBaseUrl}/v1/payments/{paymentId}/refunds")
        {
            Content = JsonContent.Create(new { amount }, options: JsonOptions)
        };
        Authorize(message, accessToken);
        message.Headers.Add("X-Idempotency-Key", idempotencyKey.ToString());

        using var response = await SendAsync(message, "refund", cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            using var json = await ReadJsonAsync(response, cancellationToken);
            return new OnlinePaymentRefundOutcome.Done(GetNumberOrString(json.RootElement, "id"));
        }

        await ThrowIfKeyRejectedAsync(response, "refund", cancellationToken);
        // Mercado Pago answered no (no balance, already refunded, too old): retrying will not change it.
        if ((int)response.StatusCode is >= 400 and < 500 and not 408 and not 429)
        {
            var (code, text) = await ReadErrorAsync(response, cancellationToken);
            _logger.LogWarning("Mercado Pago refused a refund: {Status} {Code}", (int)response.StatusCode, code);
            return new OnlinePaymentRefundOutcome.Refused(RefusedMessage(text));
        }

        throw await UnavailableAsync(response, "refund", cancellationToken);
    }

    private async Task<OnlinePaymentCredentials> TokenAsync(object body, string operation, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{_options.ApiBaseUrl}/oauth/token")
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        using var response = await SendAsync(message, operation, cancellationToken);
        if ((int)response.StatusCode is 400 or 401 or 403 or 404)
        {
            var (code, _) = await ReadErrorAsync(response, cancellationToken);
            _logger.LogWarning("Mercado Pago refused the {Operation} of an account: {Status} {Code}", operation, (int)response.StatusCode, code);
            throw new OnlinePaymentGatewayRefusedException(operation == "connect"
                ? "Mercado Pago no aceptó la autorización. Vuelve a conectar tu cuenta."
                : "Mercado Pago ya no permite usar tu cuenta: vuelve a conectarla.");
        }
        if (!response.IsSuccessStatusCode)
            throw await UnavailableAsync(response, operation, cancellationToken);

        using var json = await ReadJsonAsync(response, cancellationToken);
        var root = json.RootElement;
        var access = GetString(root, "access_token");
        var refresh = GetString(root, "refresh_token");
        var userId = root.TryGetProperty("user_id", out var user) && user.TryGetInt64(out var id) ? id : 0;
        var expiresIn = root.TryGetProperty("expires_in", out var expires) && expires.TryGetInt64(out var seconds) ? seconds : 0;
        if (string.IsNullOrWhiteSpace(access) || string.IsNullOrWhiteSpace(refresh) || userId <= 0 || expiresIn <= 0)
            throw new OnlinePaymentGatewayUnavailableException("Mercado Pago no entregó las llaves de la cuenta.");

        var liveMode = root.TryGetProperty("live_mode", out var live) && live.ValueKind == JsonValueKind.True;
        return new OnlinePaymentCredentials(userId, access, refresh, _clock.UtcNow.AddSeconds(expiresIn), liveMode);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, string operation, CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClientFactory.CreateClient(HttpClientName).SendAsync(message, cancellationToken);
        }
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException or BrokenCircuitException or TimeoutRejectedException)
                                   && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Mercado Pago did not answer the {Operation}: {Error}", operation, ex.GetType().Name);
            throw new OnlinePaymentGatewayUnavailableException("Mercado Pago no respondió. Intenta de nuevo en unos minutos.", ex);
        }
    }

    /// <summary>
    /// 401 or 403 with the account's key: the owner revoked TienditApp in Mercado Pago, or the key no longer works. Not
    /// Mercado Pago being down: retrying would only wait forever.
    /// </summary>
    private async Task ThrowIfKeyRejectedAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden))
            return;

        var (code, _) = await ReadErrorAsync(response, cancellationToken);
        _logger.LogWarning("Mercado Pago rejected the account's key in the {Operation}: {Status} {Code}", operation, (int)response.StatusCode, code);
        throw new OnlinePaymentAccountRejectedException("Mercado Pago ya no acepta la cuenta del negocio: hay que conectarla otra vez.");
    }

    private async Task<OnlinePaymentGatewayUnavailableException> UnavailableAsync(
        HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        var (code, _) = await ReadErrorAsync(response, cancellationToken);
        _logger.LogWarning("Mercado Pago failed the {Operation}: {Status} {Code}", operation, (int)response.StatusCode, code);
        return new OnlinePaymentGatewayUnavailableException("Mercado Pago no respondió. Intenta de nuevo en unos minutos.");
    }

    private static async Task<(string? Code, string? Message)> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var json = await ReadJsonAsync(response, cancellationToken);
            var root = json.RootElement;
            var code = GetString(root, "error") ?? GetNumberOrString(root, "status");
            if (root.TryGetProperty("cause", out var cause) && cause.ValueKind == JsonValueKind.Array && cause.GetArrayLength() > 0)
                code = GetNumberOrString(cause[0], "code") ?? code;
            return (code is { Length: > 60 } ? code[..60] : code, GetString(root, "message"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static OnlinePaymentDetails? ParsePayment(JsonElement payment)
    {
        var id = GetNumberOrString(payment, "id");
        var status = GetString(payment, "status");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(status))
            return null;

        var amount = payment.TryGetProperty("transaction_amount", out var value) && value.TryGetDecimal(out var number) ? number : 0m;
        DateTimeOffset? approvedAt = GetString(payment, "date_approved") is { } date
            && DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : null;
        long? collector = payment.TryGetProperty("collector_id", out var collectorId) && collectorId.TryGetInt64(out var c) ? c : null;
        var liveMode = payment.TryGetProperty("live_mode", out var live) && live.ValueKind == JsonValueKind.True;

        return new OnlinePaymentDetails(
            id, status, GetString(payment, "status_detail"), GetString(payment, "external_reference"), amount,
            GetString(payment, "currency_id"), approvedAt, collector, liveMode);
    }

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? GetNumberOrString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.String => value.GetString(),
            _ => null
        };
    }

    private static void Authorize(HttpRequestMessage message, string accessToken) =>
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

    private void EnsureConfigured()
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Mercado Pago no está configurado.");
    }

    /// <summary>The format Mercado Pago documents for its dates: milliseconds and the offset.</summary>
    private static string MercadoPagoDate(DateTimeOffset value) =>
        value.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture);

    /// <summary>What a card statement takes: letters, digits and spaces, without accents, 22 at most.</summary>
    internal static string? StatementDescriptor(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var plain = new string(text.Normalize(System.Text.NormalizationForm.FormD)
            .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            .Select(ch => char.IsAsciiLetterOrDigit(ch) || ch == ' ' ? char.ToUpperInvariant(ch) : ' ')
            .ToArray());
        var compact = SpacesPattern().Replace(plain, " ").Trim();
        if (compact.Length == 0)
            return null;
        return compact.Length > 22 ? compact[..22].TrimEnd() : compact;
    }

    private static string RefusedMessage(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "Mercado Pago no hizo la devolución.";
        var trimmed = text.Trim();
        return $"Mercado Pago no hizo la devolución: {(trimmed.Length > 200 ? trimmed[..200] : trimmed)}";
    }

    [GeneratedRegex(@"^\d{1,20}$")]
    private static partial Regex PaymentIdPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesPattern();
}
