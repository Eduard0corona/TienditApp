using FoodParkDigital.Application.WhatsApp.DTOs;

namespace FoodParkDigital.Application.Common.Interfaces;

public interface IWhatsAppNotificationService
{
    Task NotifyNewMessage(Guid tenantId, ConversationDto conversation, ChatMessageDto message);
    Task NotifyConversationUpdated(Guid tenantId, ConversationDto conversation);
}
