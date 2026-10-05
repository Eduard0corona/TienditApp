using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.WhatsApp.Entities;
using FoodParkDigital.Domain.WhatsApp.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.WhatsApp.Commands;

/// <param name="ExternalMessageId">The <c>wamid</c> the business's message got when it was sent.</param>
/// <param name="At">When Meta says it happened; the time it arrives when Meta does not say.</param>
/// <param name="ErrorCode">Meta's error code of a failed delivery (131047: the customer did not write in 24 hours).</param>
public sealed record WhatsAppStatusReport(string ExternalMessageId, MessageStatus Status, DateTimeOffset? At, string? ErrorCode = null);

/// <summary>
/// Delivery statuses that Meta reported for messages a business sent: the ticks of its chat and the result of its
/// queued campaign, reminder and notice messages. The webhook sends it in the scope of the business that owns the
/// number, so a status for a message of another business finds nothing.
/// </summary>
/// <returns>How many messages changed.</returns>
[AllowAnonymousRequest("Lo envía el procesador del webhook de WhatsApp (sistema), sin usuario.")]
public record ApplyWhatsAppStatusesCommand(Guid TenantId, IReadOnlyList<WhatsAppStatusReport> Statuses) : IRequest<int>;

public class ApplyWhatsAppStatusesCommandHandler : IRequestHandler<ApplyWhatsAppStatusesCommand, int>
{
    public const string OutsideWindowError =
        "WhatsApp no lo entregó: el cliente no escribió en las últimas 24 horas. Usa una plantilla aprobada en Meta.";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<ApplyWhatsAppStatusesCommandHandler> _logger;
    private readonly IWhatsAppNotificationService? _notifications;

    public ApplyWhatsAppStatusesCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenant,
        IDateTimeProvider clock,
        ILogger<ApplyWhatsAppStatusesCommandHandler> logger,
        IWhatsAppNotificationService? notifications = null)
    {
        _context = context;
        _tenant = tenant;
        _clock = clock;
        _logger = logger;
        _notifications = notifications;
    }

    public async Task<int> Handle(ApplyWhatsAppStatusesCommand request, CancellationToken cancellationToken)
    {
        if (request.TenantId != _tenant.TenantId)
            throw new InvalidOperationException("Delivery statuses are applied in the scope of the business that sent the messages.");

        var changed = 0;
        var chatMessages = new List<WhatsAppMessage>();
        foreach (var report in request.Statuses)
        {
            // Only this business's messages: the tenant filter applies to both.
            var message = await _context.WhatsAppMessages
                .FirstOrDefaultAsync(m => m.ExternalMessageId == report.ExternalMessageId, cancellationToken);
            if (message is not null)
            {
                message.UpdateStatus(report.Status);
                chatMessages.Add(message);
                changed++;
            }

            var outgoing = await _context.OutgoingMessages
                .FirstOrDefaultAsync(m => m.ProviderMessageId == report.ExternalMessageId, cancellationToken);
            if (outgoing is not null)
            {
                var at = report.At ?? _clock.UtcNow;
                switch (report.Status)
                {
                    case MessageStatus.Delivered:
                        outgoing.RecordDelivery(at);
                        break;
                    case MessageStatus.Read:
                        outgoing.RecordRead(at);
                        break;
                    case MessageStatus.Failed:
                        outgoing.RecordDeliveryFailure(DescribeFailure(report.ErrorCode));
                        break;
                }

                changed++;
            }
        }

        if (changed > 0)
            await _context.SaveChangesAsync(cancellationToken);

        // The chat shows the ticks as they arrive; a lost notification only waits for the next reload.
        if (_notifications is not null)
        {
            foreach (var message in chatMessages.DistinctBy(m => m.Id))
            {
                try
                {
                    await _notifications.NotifyMessageStatus(request.TenantId, message.ConversationId, message.Id, message.Status.ToString());
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        "Status of WhatsApp message {MessageId} could not be pushed to the chat: {ErrorType}",
                        message.Id,
                        ex.GetType().Name);
                }
            }
        }

        return changed;
    }

    /// <summary>Only the error code and its meaning: Meta's details can quote the recipient.</summary>
    public static string DescribeFailure(string? errorCode) => errorCode switch
    {
        "131047" => OutsideWindowError,
        null or "" => "WhatsApp no pudo entregar el mensaje.",
        _ => $"WhatsApp no pudo entregar el mensaje (código {errorCode})."
    };
}
