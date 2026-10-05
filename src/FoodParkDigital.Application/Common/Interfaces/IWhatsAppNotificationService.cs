using FoodParkDigital.Application.WhatsApp.DTOs;

namespace FoodParkDigital.Application.Common.Interfaces;

public interface IWhatsAppNotificationService
{
    Task NotifyNewMessage(Guid tenantId, ConversationDto conversation, ChatMessageDto message);
    Task NotifyConversationUpdated(Guid tenantId, ConversationDto conversation);

    /// <summary>A sent message was delivered, read or failed (the ticks of the chat).</summary>
    Task NotifyMessageStatus(Guid tenantId, Guid conversationId, Guid messageId, string status) => Task.CompletedTask;
}
