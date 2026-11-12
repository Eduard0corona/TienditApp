using FoodParkDigital.Application.WhatsApp.DTOs;

namespace FoodParkDigital.Application.Common.Interfaces;

public interface IWhatsAppNotificationService
{
    Task NotifyNewMessage(Guid tenantId, ConversationDto conversation, ChatMessageDto message);
    Task NotifyConversationUpdated(Guid tenantId, ConversationDto conversation);

    /// <summary>A sent message was delivered, read or failed (the ticks of the chat).</summary>
    Task NotifyMessageStatus(Guid tenantId, Guid conversationId, Guid messageId, string status) => Task.CompletedTask;

    /// <summary>
    /// The photo or audio of a message was kept (with where it is) or given up (Fase 31L): the chats open show it, or stop
    /// waiting for it.
    /// </summary>
    Task NotifyMessageMedia(Guid tenantId, Guid conversationId, Guid messageId, string mediaState, string? mediaPath) => Task.CompletedTask;
}
