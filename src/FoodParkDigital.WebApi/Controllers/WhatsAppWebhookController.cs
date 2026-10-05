using System.Text;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.WhatsApp.Queries;
using FoodParkDigital.Infrastructure.Services;
using FoodParkDigital.WebApi.Services;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FoodParkDigital.WebApi.Errors;

namespace FoodParkDigital.WebApi.Controllers;

[ApiController]
[Route("api/v1/whatsapp/webhook")]
[AllowAnonymous]
public class WhatsAppWebhookController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IWebhookInbox _inbox;
    private readonly WhatsAppWebhookSignatureVerifier _signatureVerifier;
    private readonly ILogger<WhatsAppWebhookController> _logger;
    private readonly bool _logSensitiveData;

    public WhatsAppWebhookController(
        IMediator mediator,
        IWebhookInbox inbox,
        WhatsAppWebhookSignatureVerifier signatureVerifier,
        IHostEnvironment environment,
        ILogger<WhatsAppWebhookController> logger)
    {
        _mediator = mediator;
        _inbox = inbox;
        _signatureVerifier = signatureVerifier;
        _logger = logger;
        // Database errors can quote row values: only Development logs them in full.
        _logSensitiveData = environment.IsDevelopment();
    }

    /// <summary>
    /// Meta webhook verification endpoint.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Verify(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge,
        CancellationToken cancellationToken)
    {
        if (mode != "subscribe" || string.IsNullOrEmpty(verifyToken) || string.IsNullOrEmpty(challenge))
            return this.ApiError(StatusCodes.Status400BadRequest, "Invalid verification request.");

        var result = await _mediator.Send(new VerifyWhatsAppWebhookTokenQuery(verifyToken), cancellationToken);
        if (result.IsFailure)
        {
            _logger.LogWarning("Webhook verification failed: unknown verify token");
            return this.ApiError(StatusCodes.Status401Unauthorized, "Invalid verify token.");
        }

        _logger.LogInformation("Webhook verified for tenant {TenantId}", result.Value);
        return Content(challenge, "text/plain", Encoding.UTF8);
    }

    /// <summary>
    /// Receives WhatsApp deliveries from Meta Cloud API. An authenticated delivery is stored in the inbox and
    /// acknowledged at once; a background worker processes it with retries (WhatsAppWebhookProcessor).
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(cancellationToken);

        // Nothing in the payload (phone_number_id included) is trusted before this check.
        var signatureStatus = _signatureVerifier.Verify(
            body, Request.Headers[WhatsAppWebhookSignatureVerifier.SignatureHeaderName]);

        // Misconfigured server, not a bad caller; Meta retries deliveries that do not get a 200.
        if (signatureStatus == WhatsAppWebhookSignatureStatus.SecretNotConfigured)
            return StatusCode(StatusCodes.Status503ServiceUnavailable);

        if (signatureStatus is not (WhatsAppWebhookSignatureStatus.Valid
            or WhatsAppWebhookSignatureStatus.VerificationDisabled))
            return this.ApiError(StatusCodes.Status401Unauthorized, "Invalid webhook signature.");

        var rawBody = Encoding.UTF8.GetString(body);
        if (string.IsNullOrWhiteSpace(rawBody))
            return Ok();

        try
        {
            // A redelivery of a stored delivery is acknowledged without storing it again. Storing ignores
            // request cancellation: once authenticated, the delivery should not be lost to a dropped connection.
            await _inbox.EnqueueAsync(WebhookSources.WhatsApp, rawBody, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Typically the database failing: without a 200, Meta redelivers it (for up to 7 days).
            _logger.LogError(LoggableException(ex), "WhatsApp webhook delivery could not be stored");
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        return Ok();
    }

    // The signature covers the exact bytes Meta sent, so they are hashed before any decoding.
    private async Task<byte[]> ReadBodyAsync(CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    private Exception LoggableException(Exception exception)
        => _logSensitiveData ? exception : LogRedaction.RedactDatabaseMessages(exception);
}
