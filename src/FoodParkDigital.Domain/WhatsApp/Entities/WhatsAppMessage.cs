using System.Text.RegularExpressions;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.WhatsApp.Enums;

namespace FoodParkDigital.Domain.WhatsApp.Entities;

public partial class WhatsAppMessage : BaseEntity
{
    /// <summary>The longest media id of WhatsApp kept; theirs are digits.</summary>
    public const int MaxMediaIdLength = 128;

    /// <summary>The longest path of a saved photo or audio ("/files/whatsapp/{negocio}/{mes}/{guid}.webp").</summary>
    public const int MaxMediaPathLength = 300;

    private WhatsAppMessage() { }

    public WhatsAppMessage(
        Guid conversationId,
        MessageDirection direction,
        string body,
        string? externalMessageId)
    {
        ConversationId = conversationId;
        Direction = direction;
        Body = body;
        ExternalMessageId = externalMessageId;
        Status = direction == MessageDirection.Outbound ? MessageStatus.Sent : MessageStatus.Delivered;
        SentAt = DateTimeOffset.UtcNow;
    }

    public Guid ConversationId { get; private set; }
    public MessageDirection Direction { get; private set; }
    public string Body { get; private set; } = null!;
    public MessageStatus Status { get; private set; }
    public string? ExternalMessageId { get; private set; }
    public DateTimeOffset SentAt { get; private set; }

    /// <summary>
    /// A photo or an audio the customer sent (Fase 31L, ADR 0092), downloaded in the background: null for text and for what
    /// stays as its marker (a video, a document, a sticker...).
    /// </summary>
    public WhatsAppMediaKind? MediaKind { get; private set; }

    /// <summary>The id WhatsApp gave the file, to download it; never shown.</summary>
    public string? MediaId { get; private set; }

    /// <summary>Where it is kept, once saved: a private file of the business that its members open with their session.</summary>
    public string? MediaPath { get; private set; }

    public WhatsAppMediaState? MediaState { get; private set; }

    public WhatsAppConversation Conversation { get; private set; } = null!;

    /// <summary>Whether <paramref name="mediaId"/> is one WhatsApp gives: letters, digits, dots, dashes or underscores.</summary>
    public static bool IsMediaId(string? mediaId)
        => !string.IsNullOrEmpty(mediaId) && mediaId.Length <= MaxMediaIdLength && MediaIdPattern().IsMatch(mediaId);

    /// <summary>The photo or audio of an inbound message, waiting to be downloaded. Only once, and only for one the customer sent.</summary>
    internal void AttachMedia(WhatsAppMediaKind kind, string mediaId)
    {
        if (Direction != MessageDirection.Inbound || MediaKind is not null)
            throw new DomainException("Solo un mensaje del cliente trae una foto o un audio, una vez.");
        if (!Enum.IsDefined(kind) || !IsMediaId(mediaId))
            throw new DomainException("La foto o el audio de WhatsApp no es válido.");

        MediaKind = kind;
        MediaId = mediaId;
        MediaState = WhatsAppMediaState.Pending;
    }

    /// <summary>Downloaded and kept at <paramref name="path"/>. A file kept already, or given up, stays as it is.</summary>
    /// <returns>Whether it changed.</returns>
    public bool SaveMedia(string path)
    {
        if (MediaState != WhatsAppMediaState.Pending)
            return false;
        if (string.IsNullOrWhiteSpace(path) || path.Length > MaxMediaPathLength)
            throw new DomainException("La dirección de la foto o el audio no es válida.");

        MediaPath = path;
        MediaState = WhatsAppMediaState.Saved;
        return true;
    }

    /// <summary>It could not be kept: only its marker stays. One kept already stays.</summary>
    /// <returns>Whether it changed.</returns>
    public bool GiveUpMedia()
    {
        if (MediaState != WhatsAppMediaState.Pending)
            return false;

        MediaState = WhatsAppMediaState.Unavailable;
        return true;
    }

    [GeneratedRegex("^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex MediaIdPattern();

    public void UpdateStatus(MessageStatus status)
    {
        if (Status == MessageStatus.Failed)
            return;

        if (status == MessageStatus.Failed)
        {
            Status = status;
            return;
        }

        if (status > Status)
            Status = status;
    }
}
