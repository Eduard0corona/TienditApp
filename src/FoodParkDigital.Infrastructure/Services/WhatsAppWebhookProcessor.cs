using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.WhatsApp.Commands;
using FoodParkDigital.Domain.WhatsApp.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Infrastructure.Services;

/// <param name="FailedItems">Messages or status batches that could not be stored; the delivery is retried.</param>
/// <param name="InvalidPayload">The body is not JSON: a retry would carry the same body, so it is dropped.</param>
public sealed record WhatsAppWebhookOutcome(int FailedItems, bool InvalidPayload);

/// <summary>
/// Reads a Meta Cloud API webhook delivery (already authenticated and stored in the inbox): routes each
/// inbound message to the tenant that owns its <c>phone_number_id</c> and applies delivery statuses.
/// </summary>
public sealed class WhatsAppWebhookProcessor
{
    private readonly ISender _sender;
    private readonly IApplicationDbContext _context;
    private readonly ILogger<WhatsAppWebhookProcessor> _logger;
    private readonly bool _logSensitiveData;

    public WhatsAppWebhookProcessor(
        ISender sender,
        IApplicationDbContext context,
        IHostEnvironment environment,
        ILogger<WhatsAppWebhookProcessor> logger)
    {
        _sender = sender;
        _context = context;
        _logger = logger;
        // Database errors can quote the sender's phone number: only Development logs them in full.
        _logSensitiveData = environment.IsDevelopment();
    }

    public async Task<WhatsAppWebhookOutcome> ProcessAsync(string rawBody, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
            return new WhatsAppWebhookOutcome(0, InvalidPayload: false);

        JsonDocument payloadDoc;
        try
        {
            payloadDoc = JsonDocument.Parse(rawBody);
        }
        catch (JsonException ex)
        {
            _logger.LogError(LoggableException(ex), "WhatsApp webhook payload is not valid JSON");
            return new WhatsAppWebhookOutcome(0, InvalidPayload: true);
        }

        using (payloadDoc)
        {
            var payload = payloadDoc.RootElement;
            if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty("entry", out var entries)
                || entries.ValueKind != JsonValueKind.Array)
                return new WhatsAppWebhookOutcome(0, InvalidPayload: false);

            var failed = 0;
            foreach (var entry in entries.EnumerateArray())
            {
                if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var change in changes.EnumerateArray())
                {
                    if (!change.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Object)
                        continue;

                    var phoneNumberId = TryGetPhoneNumberId(value);
                    if (string.IsNullOrEmpty(phoneNumberId))
                        continue;

                    // The webhook is the entry point that discovers the tenant: its phone_number_id is unique
                    // across tenants (filtered unique index), so this lookup crosses the tenant filter on purpose.
                    var tenantId = await _context.WhatsAppConfigs
                        .IgnoreQueryFilters()
                        .AsNoTracking()
                        .Where(c => c.PhoneNumberId == phoneNumberId)
                        .Select(c => (Guid?)c.TenantId)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (tenantId is null)
                    {
                        _logger.LogWarning("Webhook received for unknown PhoneNumberId: {PhoneNumberId}", phoneNumberId);
                        continue;
                    }

                    failed += await ProcessMessagesAsync(value, tenantId.Value, TryGetProfileName(value));
                    failed += await ProcessStatusesAsync(value, cancellationToken);
                }
            }

            return new WhatsAppWebhookOutcome(failed, InvalidPayload: false);
        }
    }

    /// <returns>How many messages could not be stored.</returns>
    private async Task<int> ProcessMessagesAsync(JsonElement value, Guid tenantId, string? profileName)
    {
        if (!value.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
            return 0;

        var failed = 0;
        foreach (var msg in messages.EnumerateArray())
        {
            var from = msg.TryGetProperty("from", out var fromProp) ? fromProp.GetString() : null;
            var msgId = msg.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
            var body = ExtractInboundBody(msg);

            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(msgId) || string.IsNullOrWhiteSpace(body))
                continue;

            // A failed message must not keep the rest of the delivery from being stored. A message that started
            // is allowed to finish (auto-reply included): a retry would be deduplicated by its id.
            try
            {
                await _sender.Send(new ProcessInboundMessageCommand
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
                    "WhatsApp message {ExternalMessageId} for tenant {TenantId} could not be processed; the delivery will be retried",
                    msgId,
                    tenantId);
                failed++;
            }
        }

        return failed;
    }

    /// <returns>1 when the statuses could not be saved, 0 otherwise.</returns>
    private async Task<int> ProcessStatusesAsync(JsonElement value, CancellationToken cancellationToken)
    {
        if (!value.TryGetProperty("statuses", out var statuses) || statuses.ValueKind != JsonValueKind.Array)
            return 0;

        try
        {
            var anyUpdated = false;
            foreach (var statusEvent in statuses.EnumerateArray())
            {
                var externalId = statusEvent.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                var statusRaw = statusEvent.TryGetProperty("status", out var statusProp) ? statusProp.GetString() : null;
                if (string.IsNullOrWhiteSpace(externalId) || string.IsNullOrWhiteSpace(statusRaw))
                    continue;

                if (!TryMapStatus(statusRaw, out var mappedStatus))
                    continue;

                // Provider message ids are unique across tenants (filtered unique index).
                var message = await _context.WhatsAppMessages
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(m => m.ExternalMessageId == externalId, cancellationToken);
                if (message is not null)
                {
                    message.UpdateStatus(mappedStatus);
                    anyUpdated = true;
                }

                // Campaign and reminder messages keep their own delivery result.
                var outgoing = await _context.OutgoingMessages
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(m => m.ProviderMessageId == externalId, cancellationToken);
                if (outgoing is not null)
                {
                    var at = TryGetTimestamp(statusEvent) ?? DateTimeOffset.UtcNow;
                    switch (mappedStatus)
                    {
                        case MessageStatus.Delivered:
                            outgoing.RecordDelivery(at);
                            break;
                        case MessageStatus.Read:
                            outgoing.RecordRead(at);
                            break;
                        case MessageStatus.Failed:
                            outgoing.RecordDeliveryFailure(DescribeDeliveryFailure(statusEvent));
                            break;
                    }

                    anyUpdated = true;
                }
            }

            if (anyUpdated)
                await _context.SaveChangesAsync(cancellationToken);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(LoggableException(ex), "WhatsApp delivery statuses could not be saved; the delivery will be retried");
            return 1;
        }
    }

    private static string? TryGetPhoneNumberId(JsonElement value)
    {
        if (!value.TryGetProperty("metadata", out var metadata) || metadata.ValueKind != JsonValueKind.Object)
            return null;
        return metadata.TryGetProperty("phone_number_id", out var phoneNumberId) ? phoneNumberId.GetString() : null;
    }

    private static string? TryGetProfileName(JsonElement value)
    {
        if (!value.TryGetProperty("contacts", out var contacts) || contacts.ValueKind != JsonValueKind.Array)
            return null;

        var contact = contacts.EnumerateArray().FirstOrDefault();
        return contact.ValueKind == JsonValueKind.Object
            && contact.TryGetProperty("profile", out var profile)
            && profile.TryGetProperty("name", out var name)
                ? name.GetString()
                : null;
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

    /// <summary>Unix seconds of the status, as Meta sends it ("timestamp": "1700000000").</summary>
    private static DateTimeOffset? TryGetTimestamp(JsonElement statusEvent)
    {
        if (!statusEvent.TryGetProperty("timestamp", out var timestamp))
            return null;

        var raw = timestamp.ValueKind == JsonValueKind.String ? timestamp.GetString() : timestamp.GetRawText();
        return long.TryParse(raw, out var seconds) && seconds > 0
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
    }

    /// <summary>Only the error code and its generic title: the details can quote the recipient.</summary>
    private static string DescribeDeliveryFailure(JsonElement statusEvent)
    {
        if (statusEvent.TryGetProperty("errors", out var errors)
            && errors.ValueKind == JsonValueKind.Array
            && errors.GetArrayLength() > 0)
        {
            var error = errors[0];
            var code = error.TryGetProperty("code", out var codeProp) ? codeProp.GetRawText() : null;
            if (code == "131047")
                return "WhatsApp no lo entregó: el cliente no escribió en las últimas 24 horas. Usa una plantilla aprobada en Meta.";
            if (code is not null)
                return $"WhatsApp no pudo entregar el mensaje (código {code}).";
        }

        return "WhatsApp no pudo entregar el mensaje.";
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

    private Exception LoggableException(Exception exception)
        => _logSensitiveData ? exception : LogRedaction.RedactDatabaseMessages(exception);
}
