using System.Text.Json;
using System.Text.RegularExpressions;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Infrastructure.Services;
using FoodParkDigital.WebApi.Errors;
using FoodParkDigital.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoodParkDigital.WebApi.Controllers;

/// <summary>
/// Mercado Pago's notifications of the payments into the accounts the businesses connected (Fase 24I, ADR 0031): the
/// webhook of TienditApp's application, event «Pagos». A signed notification is stored in the inbox and acknowledged at
/// once; the processor reads the payment from Mercado Pago with each business's key (MercadoPagoWebhookProcessor). The
/// signature covers only the notified id, so only that id (and the account it says, to find the business) is kept.
/// </summary>
[ApiController]
[Route("api/v1/payments/mercadopago/webhook")]
[AllowAnonymous]
public partial class MercadoPagoWebhookController : ControllerBase
{
    private const int MaxBodyBytes = 64 * 1024;

    private readonly IWebhookInbox _inbox;
    private readonly MercadoPagoWebhookSignatureVerifier _signatureVerifier;
    private readonly ILogger<MercadoPagoWebhookController> _logger;
    private readonly bool _logSensitiveData;

    public MercadoPagoWebhookController(
        IWebhookInbox inbox,
        MercadoPagoWebhookSignatureVerifier signatureVerifier,
        IHostEnvironment environment,
        ILogger<MercadoPagoWebhookController> logger)
    {
        _inbox = inbox;
        _signatureVerifier = signatureVerifier;
        _logger = logger;
        // Database errors can quote row values: only Development logs them in full.
        _logSensitiveData = environment.IsDevelopment();
    }

    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        var dataId = Request.Query["data.id"].ToString();
        var signatureStatus = _signatureVerifier.Verify(
            dataId,
            Request.Headers[MercadoPagoWebhookSignatureVerifier.SignatureHeaderName],
            Request.Headers[MercadoPagoWebhookSignatureVerifier.RequestIdHeaderName]);

        // Misconfigured server, not a bad caller; Mercado Pago retries notifications that do not get a 200.
        if (signatureStatus == MercadoPagoWebhookSignatureStatus.SecretNotConfigured)
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        if (signatureStatus != MercadoPagoWebhookSignatureStatus.Valid)
            return this.ApiError(StatusCodes.Status401Unauthorized, "Invalid webhook signature.");

        var body = await ReadBodyAsync(cancellationToken);
        var notification = Read(body);
        var type = Request.Query["type"].ToString() is { Length: > 0 } queryType ? queryType : notification.Type;
        // Only payments matter, and Mercado Pago's payment ids are numbers.
        if (type != "payment" || !PaymentIdPattern().IsMatch(dataId))
            return Ok();

        var requestId = Request.Headers[MercadoPagoWebhookSignatureVerifier.RequestIdHeaderName].ToString();
        var payload = JsonSerializer.Serialize(new { paymentId = dataId, userId = notification.UserId, requestId });
        try
        {
            // Storing ignores request cancellation: once authenticated, the notification should not be lost. The signature
            // covers the payment and the request only, so they alone identify the delivery: the same signed notification
            // sent again with another account in its body is the same one, not a new round of calls to Mercado Pago.
            await _inbox.EnqueueAsync(WebhookSources.MercadoPago, payload, $"{dataId}:{requestId}", CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Typically the database failing: without a 200, Mercado Pago notifies again.
            _logger.LogError(LoggableException(ex), "Mercado Pago notification could not be stored");
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        return Ok();
    }

    private async Task<string> ReadBodyAsync(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var buffer = new char[MaxBodyBytes];
        var read = await reader.ReadBlockAsync(buffer.AsMemory(), cancellationToken);
        return new string(buffer, 0, read);
    }

    /// <summary>The type and the account a notification says; neither is signed.</summary>
    private static (string? Type, long? UserId) Read(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return (null, null);
            var type = root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            long? userId = root.TryGetProperty("user_id", out var u)
                ? u.ValueKind switch
                {
                    JsonValueKind.Number when u.TryGetInt64(out var number) => number,
                    JsonValueKind.String when long.TryParse(u.GetString(), out var text) => text,
                    _ => null
                }
                : null;
            return (type, userId);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private Exception LoggableException(Exception exception)
        => _logSensitiveData ? exception : LogRedaction.RedactDatabaseMessages(exception);

    [GeneratedRegex(@"^\d{1,20}$")]
    private static partial Regex PaymentIdPattern();
}
