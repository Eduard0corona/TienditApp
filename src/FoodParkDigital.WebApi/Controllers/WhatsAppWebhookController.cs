using System.Text;
using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.WhatsApp.Commands;
using FoodParkDigital.Application.WhatsApp.Queries;
using FoodParkDigital.Domain.WhatsApp.Enums;
using FoodParkDigital.Infrastructure.Services;
using FoodParkDigital.WebApi.Services;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.WebApi.Controllers;

[ApiController]
[Route("api/v1/whatsapp/webhook")]
[AllowAnonymous]
public class WhatsAppWebhookController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IApplicationDbContext _context;
    private readonly WhatsAppWebhookSignatureVerifier _signatureVerifier;
    private readonly ILogger<WhatsAppWebhookController> _logger;
    private readonly bool _logSensitiveData;

    public WhatsAppWebhookController(
        IMediator mediator,
        IApplicationDbContext context,
        WhatsAppWebhookSignatureVerifier signatureVerifier,
        IHostEnvironment environment,
        ILogger<WhatsAppWebhookController> logger)
    {
        _mediator = mediator;
        _context = context;
        _signatureVerifier = signatureVerifier;
        _logger = logger;
        // Database errors can quote the sender's phone number: only Development logs them in full.
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
            return BadRequest("Invalid verification request.");

        var result = await _mediator.Send(new VerifyWhatsAppWebhookTokenQuery(verifyToken), cancellationToken);
        if (result.IsFailure)
        {
            _logger.LogWarning("Webhook verification failed: unknown verify token");
            return Unauthorized("Invalid verify token.");
        }

        _logger.LogInformation("Webhook verified for tenant {TenantId}", result.Value);
        return Content(challenge, "text/plain", Encoding.UTF8);
    }

    /// <summary>
    /// Receives incoming WhatsApp messages from Meta Cloud API.
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
            return Unauthorized("Invalid webhook signature.");

        // Processing deliberately ignores request cancellation: an authenticated delivery
        // should finish (auto-reply included) because Meta's retry would be deduplicated.
        try
        {
            var rawBody = Encoding.UTF8.GetString(body);
            if (string.IsNullOrWhiteSpace(rawBody))
                return Ok();

            using var payloadDoc = JsonDocument.Parse(rawBody);
            var payload = payloadDoc.RootElement;

            if (!payload.TryGetProperty("entry", out var entries))
                return Ok(); // Acknowledge but ignore

            var allMessagesProcessed = true;
            foreach (var entry in entries.EnumerateArray())
            {
                if (!entry.TryGetProperty("changes", out var changes))
                    continue;

                foreach (var change in changes.EnumerateArray())
                {
                    if (!change.TryGetProperty("value", out var value))
                        continue;

                    var phoneNumberId = TryGetPhoneNumberId(value);

                    if (string.IsNullOrEmpty(phoneNumberId))
                        continue;

                    // Find tenant by phone number ID
                    var config = await _context.WhatsAppConfigs
                        .IgnoreQueryFilters()
                        .AsNoTracking()
                        .FirstOrDefaultAsync(c => c.PhoneNumberId == phoneNumberId);

                    if (config is null)
                    {
                        _logger.LogWarning("Webhook received for unknown PhoneNumberId: {PhoneNumberId}", phoneNumberId);
                        continue;
                    }

                    // Extract contact name
                    string? profileName = null;
                    if (value.TryGetProperty("contacts", out var contacts))
                    {
                        var contact = contacts.EnumerateArray().FirstOrDefault();
                        if (contact.ValueKind != JsonValueKind.Undefined &&
                            contact.TryGetProperty("profile", out var profile) &&
                            profile.TryGetProperty("name", out var name))
                        {
                            profileName = name.GetString();
                        }
                    }

                    if (!await ProcessMessagesAsync(value, config.TenantId, profileName))
                        allMessagesProcessed = false;

                    await ProcessStatusesAsync(value);
                }
            }

            // Meta redelivers anything that does not get a 200, for up to 7 days. The messages this
            // delivery did store are then ignored as duplicates, so only the failed ones run again.
            if (!allMessagesProcessed)
                return StatusCode(StatusCodes.Status500InternalServerError);
        }
        catch (JsonException ex)
        {
            // A redelivery would carry the same body, so it is acknowledged instead of retried.
            _logger.LogError(LoggableException(ex), "WhatsApp webhook payload is not valid JSON");
        }
        catch (Exception ex)
        {
            // Typically the database failing; the redelivery is deduplicated message by message.
            _logger.LogError(LoggableException(ex), "Error processing WhatsApp webhook payload");
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        return Ok();
    }

    /// <summary>
    /// Processes every inbound message of the change and returns <c>false</c> when any of them
    /// failed, so the delivery is answered with an error and Meta sends it again.
    /// </summary>
    private async Task<bool> ProcessMessagesAsync(JsonElement value, Guid tenantId, string? profileName)
    {
        if (!value.TryGetProperty("messages", out var messages))
            return true;

        var allProcessed = true;
        foreach (var msg in messages.EnumerateArray())
        {
            var from = msg.TryGetProperty("from", out var fromProp) ? fromProp.GetString() : null;
            var msgId = msg.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
            var body = ExtractInboundBody(msg);

            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(msgId) || string.IsNullOrWhiteSpace(body))
                continue;

            // A failed message must not keep the rest of the payload from being stored.
            try
            {
                await _mediator.Send(new ProcessInboundMessageCommand
                {
                    TenantId = tenantId,
                    FromPhone = from.StartsWith('+') ? from : $"+{from}",
                    ProfileName = profileName,
                    Body = body,
                    ExternalMessageId = msgId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    LoggableException(ex),
                    "WhatsApp message {ExternalMessageId} for tenant {TenantId} could not be processed; Meta will redeliver it",
                    msgId,
                    tenantId);
                allProcessed = false;
            }
        }

        return allProcessed;
    }

    private async Task ProcessStatusesAsync(JsonElement value)
    {
        if (!value.TryGetProperty("statuses", out var statuses))
            return;

        var anyUpdated = false;
        foreach (var statusEvent in statuses.EnumerateArray())
        {
            var externalId = statusEvent.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
            var statusRaw = statusEvent.TryGetProperty("status", out var statusProp) ? statusProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(externalId) || string.IsNullOrWhiteSpace(statusRaw))
                continue;

            if (!TryMapStatus(statusRaw, out var mappedStatus))
                continue;

            var message = await _context.WhatsAppMessages
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.ExternalMessageId == externalId);
            if (message is null)
                continue;

            message.UpdateStatus(mappedStatus);
            anyUpdated = true;
        }

        if (anyUpdated)
            await _context.SaveChangesAsync();
    }

    private string? TryGetPhoneNumberId(JsonElement value)
    {
        if (!value.TryGetProperty("metadata", out var metadata))
            return null;
        if (!metadata.TryGetProperty("phone_number_id", out var phoneNumberId))
            return null;
        return phoneNumberId.GetString();
    }

    private static string? ExtractInboundBody(JsonElement message)
    {
        var type = message.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;
        return type switch
        {
            "text" when message.TryGetProperty("text", out var text)
                && text.TryGetProperty("body", out var body) => body.GetString(),
            "button" when message.TryGetProperty("button", out var button)
                && button.TryGetProperty("text", out var btnText) => btnText.GetString(),
            "interactive" => ExtractInteractiveText(message),
            _ => null
        };
    }

    private static string? ExtractInteractiveText(JsonElement message)
    {
        if (!message.TryGetProperty("interactive", out var interactive))
            return null;

        if (interactive.TryGetProperty("button_reply", out var buttonReply)
            && buttonReply.TryGetProperty("title", out var buttonTitle))
            return buttonTitle.GetString();

        if (interactive.TryGetProperty("list_reply", out var listReply)
            && listReply.TryGetProperty("title", out var listTitle))
            return listTitle.GetString();

        return null;
    }

    private static bool TryMapStatus(string rawStatus, out MessageStatus status)
    {
        var normalized = rawStatus.ToLowerInvariant();
        status = normalized switch
        {
            "sent" => MessageStatus.Sent,
            "delivered" => MessageStatus.Delivered,
            "read" => MessageStatus.Read,
            "failed" => MessageStatus.Failed,
            _ => default
        };

        return normalized is "sent" or "delivered" or "read" or "failed";
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
