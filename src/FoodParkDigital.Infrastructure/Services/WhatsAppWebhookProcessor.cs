using System.Globalization;
using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.WhatsApp.Commands;
using FoodParkDigital.Domain.WhatsApp.Enums;
using FoodParkDigital.Infrastructure.BackgroundJobs;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Infrastructure.Services;

/// <param name="FailedItems">Messages or status batches that could not be stored; the delivery is retried.</param>
/// <param name="InvalidPayload">The body is not JSON: a retry would carry the same body, so it is dropped.</param>
public sealed record WhatsAppWebhookOutcome(int FailedItems, bool InvalidPayload);

/// <summary>
/// Reads a Meta Cloud API webhook delivery (already authenticated and stored in the inbox). It finds the business
/// that owns each <c>phone_number_id</c> (the one read across businesses) and processes that part of the delivery
/// in the business's own scope: its inbound messages and the delivery statuses of what it sent, with the tenant
/// filter on as in one of its requests.
/// </summary>
public sealed class WhatsAppWebhookProcessor
{
    private const string WebhookActor = "whatsapp-webhook";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IApplicationDbContext _context;
    private readonly ILogger<WhatsAppWebhookProcessor> _logger;
    private readonly bool _logSensitiveData;

    public WhatsAppWebhookProcessor(
        IServiceScopeFactory scopeFactory,
        IApplicationDbContext context,
        IHostEnvironment environment,
        ILogger<WhatsAppWebhookProcessor> logger)
    {
        _scopeFactory = scopeFactory;
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

                    // Each business's part in its own scope (and DbContext): its filters apply, and a failure leaves
                    // nothing half-tracked for another business of the same delivery.
                    await using var scope = _scopeFactory.CreateTenantScope(tenantId.Value, WebhookActor);
                    failed += await ProcessMessagesAsync(scope.ServiceProvider, value, tenantId.Value, TryGetProfileName(value));
                    failed += await ProcessStatusesAsync(scope.ServiceProvider, value, tenantId.Value, cancellationToken);
                }
            }

            return new WhatsAppWebhookOutcome(failed, InvalidPayload: false);
        }
    }

    /// <returns>How many messages could not be stored.</returns>
    private async Task<int> ProcessMessagesAsync(IServiceProvider services, JsonElement value, Guid tenantId, string? profileName)
    {
        if (!value.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
            return 0;

        var sender = services.GetRequiredService<ISender>();
        var failed = 0;
        foreach (var msg in messages.EnumerateArray())
        {
            var from = msg.TryGetProperty("from", out var fromProp) ? fromProp.GetString() : null;
            var msgId = msg.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
            var (body, isMedia) = ExtractInboundBody(msg);

            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(msgId) || string.IsNullOrWhiteSpace(body))
                continue;

            // A failed message must not keep the rest of the delivery from being stored. A message that started
            // is allowed to finish (auto-reply included): a retry would be deduplicated by its id.
            try
            {
                await sender.Send(new ProcessInboundMessageCommand
                {
                    TenantId = tenantId,
                    FromPhone = from.StartsWith('+') ? from : $"+{from}",
                    ProfileName = profileName,
                    Body = body,
                    ExternalMessageId = msgId,
                    IsMedia = isMedia
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
    private async Task<int> ProcessStatusesAsync(
        IServiceProvider services, JsonElement value, Guid tenantId, CancellationToken cancellationToken)
    {
        if (!value.TryGetProperty("statuses", out var statuses) || statuses.ValueKind != JsonValueKind.Array)
            return 0;

        var context = services.GetRequiredService<IApplicationDbContext>();
        var notifications = services.GetService<IWhatsAppNotificationService>();
        try
        {
            var anyUpdated = false;
            var chatMessages = new List<Domain.WhatsApp.Entities.WhatsAppMessage>();
            foreach (var statusEvent in statuses.EnumerateArray())
            {
                var externalId = statusEvent.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                var statusRaw = statusEvent.TryGetProperty("status", out var statusProp) ? statusProp.GetString() : null;
                if (string.IsNullOrWhiteSpace(externalId) || string.IsNullOrWhiteSpace(statusRaw))
                    continue;

                if (!TryMapStatus(statusRaw, out var mappedStatus))
                    continue;

                // Only this business's messages: a status reported to its number for someone else's id changes nothing.
                var message = await context.WhatsAppMessages
                    .FirstOrDefaultAsync(m => m.ExternalMessageId == externalId, cancellationToken);
                if (message is not null)
                {
                    message.UpdateStatus(mappedStatus);
                    chatMessages.Add(message);
                    anyUpdated = true;
                }

                // Campaign, reminder and notice messages keep their own delivery result.
                var outgoing = await context.OutgoingMessages
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
                await context.SaveChangesAsync(cancellationToken);

            // The chat shows the ticks as they arrive; a lost notification only waits for the next reload.
            if (notifications is not null)
            {
                foreach (var message in chatMessages.DistinctBy(m => m.Id))
                {
                    try
                    {
                        await notifications.NotifyMessageStatus(tenantId, message.ConversationId, message.Id, message.Status.ToString());
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Status of WhatsApp message {MessageId} could not be pushed to the chat: {ErrorType}", message.Id, ex.GetType().Name);
                    }
                }
            }
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

    /// <summary>
    /// The text of the message, or a marker for what the chat cannot show yet (photo, voice note, location...) so
    /// the conversation keeps every message the customer sent. Reactions and system notices are skipped.
    /// </summary>
    private static (string? Body, bool IsMedia) ExtractInboundBody(JsonElement message)
    {
        var type = message.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;
        return type switch
        {
            "text" when message.TryGetProperty("text", out var text)
                && text.TryGetProperty("body", out var body) => (body.GetString(), false),
            "button" when message.TryGetProperty("button", out var button)
                && button.TryGetProperty("text", out var btnText) => (btnText.GetString(), false),
            "interactive" => (ExtractInteractiveText(message), false),
            "image" => (WithCaption("📷 Foto", message, "image"), true),
            "video" => (WithCaption("🎬 Video", message, "video"), true),
            "audio" => (IsVoiceNote(message) ? "🎤 Nota de voz" : "🎵 Audio", true),
            "document" => (DescribeDocument(message), true),
            "sticker" => ("🙂 Sticker", true),
            "location" => (DescribeLocation(message), true),
            "contacts" => (DescribeContacts(message), true),
            "order" => ("🛒 Pedido desde el catálogo de WhatsApp", true),
            "reaction" or "system" => (null, false),
            null => (null, false),
            _ => ("⚠️ Mensaje que no se puede mostrar aquí; ábrelo en WhatsApp.", true)
        };
    }

    private static string? StringProperty(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string WithCaption(string marker, JsonElement message, string type)
    {
        var caption = message.TryGetProperty(type, out var media) ? StringProperty(media, "caption") : null;
        return string.IsNullOrWhiteSpace(caption) ? marker : $"{marker}: {caption.Trim()}";
    }

    private static bool IsVoiceNote(JsonElement message)
        => message.TryGetProperty("audio", out var audio)
           && audio.ValueKind == JsonValueKind.Object
           && audio.TryGetProperty("voice", out var voice)
           && voice.ValueKind == JsonValueKind.True;

    private static string DescribeDocument(JsonElement message)
    {
        if (!message.TryGetProperty("document", out var document))
            return "📄 Documento";
        var name = StringProperty(document, "filename");
        var caption = StringProperty(document, "caption");
        var text = string.IsNullOrWhiteSpace(name) ? "📄 Documento" : $"📄 Documento: {name.Trim()}";
        return string.IsNullOrWhiteSpace(caption) ? text : $"{text} ({caption.Trim()})";
    }

    /// <summary>The place and a map link: deliveries go straight to it.</summary>
    private static string DescribeLocation(JsonElement message)
    {
        if (!message.TryGetProperty("location", out var location) || location.ValueKind != JsonValueKind.Object)
            return "📍 Ubicación";

        var label = string.Join(", ", new[] { StringProperty(location, "name"), StringProperty(location, "address") }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim()));
        string? map = null;
        if (location.TryGetProperty("latitude", out var lat) && lat.ValueKind == JsonValueKind.Number
            && location.TryGetProperty("longitude", out var lng) && lng.ValueKind == JsonValueKind.Number)
            map = string.Create(CultureInfo.InvariantCulture, $"https://maps.google.com/?q={lat.GetDouble():0.######},{lng.GetDouble():0.######}");

        return (label.Length > 0, map) switch
        {
            (true, not null) => $"📍 Ubicación: {label} {map}",
            (true, null) => $"📍 Ubicación: {label}",
            (false, not null) => $"📍 Ubicación: {map}",
            _ => "📍 Ubicación"
        };
    }

    private static string DescribeContacts(JsonElement message)
    {
        if (!message.TryGetProperty("contacts", out var contacts) || contacts.ValueKind != JsonValueKind.Array)
            return "👤 Contacto";
        var names = contacts.EnumerateArray()
            .Select(contact => contact.TryGetProperty("name", out var name) ? StringProperty(name, "formatted_name") : null)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();
        return names.Count == 0 ? "👤 Contacto" : $"👤 Contacto: {string.Join(", ", names)}";
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
