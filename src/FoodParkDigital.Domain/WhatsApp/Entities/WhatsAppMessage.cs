using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.WhatsApp.Enums;

namespace FoodParkDigital.Domain.WhatsApp.Entities;

public class WhatsAppMessage : BaseEntity
{
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

    public WhatsAppConversation Conversation { get; private set; } = null!;

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
