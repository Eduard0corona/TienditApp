using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.OnlineBooking;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.WhatsApp.Entities;
using FoodParkDigital.Domain.WhatsApp.Enums;
using FoodParkDigital.Domain.WhatsApp.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.WhatsApp.Media;

/// <summary>
/// Downloads from WhatsApp the photo or audio a customer sent (Fase 31L, ADR 0092) and keeps it in the business's private
/// storage, then tells the chats open. A photo is read for what it is and saved again as WebP without its metadata; an audio
/// is kept as it came only if it is one a browser plays. One too big, of another kind, or that WhatsApp no longer has is
/// given up: the message keeps its marker. When WhatsApp does not answer now it throws and the outbox tries again; on the
/// last try it gives up instead, so the chat never waits forever.
/// </summary>
public sealed class WhatsAppMediaDownloadHandler : IOutboxEventHandler<WhatsAppMediaReceivedEvent>
{
    private readonly IApplicationDbContext _context;
    private readonly IWhatsAppMediaDownloader _downloader;
    private readonly IPrivateFileStorage _storage;
    private readonly IImageNormalizationService _images;
    private readonly PublicPhotoGate _gate;
    private readonly IWhatsAppNotificationService _notifications;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<WhatsAppMediaDownloadHandler> _logger;

    public WhatsAppMediaDownloadHandler(
        IApplicationDbContext context,
        IWhatsAppMediaDownloader downloader,
        IPrivateFileStorage storage,
        IImageNormalizationService images,
        PublicPhotoGate gate,
        IWhatsAppNotificationService notifications,
        IDateTimeProvider clock,
        ILogger<WhatsAppMediaDownloadHandler> logger)
    {
        _context = context;
        _downloader = downloader;
        _storage = storage;
        _images = images;
        _gate = gate;
        _notifications = notifications;
        _clock = clock;
        _logger = logger;
    }

    public async Task HandleAsync(WhatsAppMediaReceivedEvent domainEvent, OutboxEventContext context, CancellationToken cancellationToken)
    {
        if (context.TenantId is not { } tenantId || tenantId != domainEvent.TenantId)
            return;

        var message = await _context.WhatsAppMessages
            .Where(m => m.Id == domainEvent.MessageId && m.Conversation.TenantId == tenantId)
            .FirstOrDefaultAsync(cancellationToken);
        // Kept or given up already (a retry after the save): nothing to do.
        if (message is not { MediaState: WhatsAppMediaState.Pending, MediaKind: { } kind, MediaId: { } mediaId })
            return;

        string? path;
        try
        {
            path = await KeepAsync(tenantId, kind, mediaId, context, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && context.IsLastAttempt)
        {
            _logger.LogWarning(
                "The {MediaKind} of WhatsApp message {MessageId} of tenant {TenantId} could not be kept after its last attempt: {ErrorType}",
                kind,
                message.Id,
                tenantId,
                ex.GetType().Name);
            path = null;
        }

        var changed = path is null ? message.GiveUpMedia() : message.SaveMedia(path);
        if (!changed)
            return;

        await _context.SaveChangesAsync(cancellationToken);
        // Best effort: a chat that misses it reads it when it opens the conversation again.
        try
        {
            await _notifications.NotifyMessageMedia(tenantId, message.ConversationId, message.Id, message.MediaState!.Value.ToString(), message.MediaPath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("The chats of tenant {TenantId} were not told of the media of message {MessageId}: {ErrorType}", tenantId, message.Id, ex.GetType().Name);
        }
    }

    /// <returns>Where it was kept, or null when it is given up.</returns>
    private async Task<string?> KeepAsync(
        Guid tenantId, WhatsAppMediaKind kind, string mediaId, OutboxEventContext context, CancellationToken cancellationToken)
    {
        var download = await _downloader.DownloadAsync(mediaId, WhatsAppMediaRules.MaxBytes(kind), cancellationToken);
        switch (download.Outcome)
        {
            case WhatsAppMediaDownloadOutcome.Transient:
                throw new WhatsAppMediaNotReadyException("WhatsApp did not answer now.");
            case WhatsAppMediaDownloadOutcome.Unavailable:
                return null;
        }

        var content = download.Content!;
        if (content.LongLength == 0 || content.LongLength > WhatsAppMediaRules.MaxBytes(kind))
            return null;

        var key = WhatsAppMediaRules.Folder(tenantId, _clock.UtcNow) + "/" + Guid.NewGuid();
        if (kind == WhatsAppMediaKind.Image)
        {
            if (!FileSignature.IsImage(FileSignature.Detect(content.AsSpan(0, Math.Min(content.Length, 12)))))
                return null;

            using var slot = await _gate.EnterAsync(cancellationToken)
                ?? throw new WhatsAppMediaNotReadyException("Other photos are being read now.");
            Stream photo;
            try
            {
                // Re-encoding drops anything hidden in the file and the photo's metadata (where it was taken, the device).
                photo = await _images.NormalizeToWebpAsync(new MemoryStream(content, writable: false), WhatsAppMediaRules.MaxImageDimension, cancellationToken);
            }
            catch (DomainException)
            {
                return null;
            }

            await using (photo)
            {
                key += ".webp";
                await _storage.SaveAsync(key, photo, "image/webp", cancellationToken);
            }
        }
        else
        {
            if (WhatsAppMediaRules.DetectAudio(content.AsSpan(0, Math.Min(content.Length, 12))) is not { } audio)
                return null;

            key += audio.Extension;
            await using var stream = new MemoryStream(content, writable: false);
            await _storage.SaveAsync(key, stream, audio.ContentType, cancellationToken);
        }

        return OrderUploadRules.ToPrivateFilePath(key);
    }
}

/// <summary>WhatsApp, or this instance, cannot take the file now: the outbox tries again later.</summary>
public sealed class WhatsAppMediaNotReadyException(string message) : Exception(message);
