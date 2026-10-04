using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.WhatsApp.DTOs;
using FoodParkDigital.WebApi.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace FoodParkDigital.WebApi.Services;

public class WhatsAppNotificationService : IWhatsAppNotificationService
{
    private readonly IHubContext<ChatHub> _hubContext;

    public WhatsAppNotificationService(IHubContext<ChatHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task NotifyNewMessage(Guid tenantId, ConversationDto conversation, ChatMessageDto message)
    {
        await _hubContext.Clients
            .Group($"chat_{tenantId}")
            .SendAsync("NewMessage", new { Conversation = conversation, Message = message });
    }

    public async Task NotifyConversationUpdated(Guid tenantId, ConversationDto conversation)
    {
        await _hubContext.Clients
            .Group($"chat_{tenantId}")
            .SendAsync("ConversationUpdated", conversation);
    }

    public async Task NotifyMessageStatus(Guid tenantId, Guid conversationId, Guid messageId, string status)
    {
        await _hubContext.Clients
            .Group($"chat_{tenantId}")
            .SendAsync("MessageStatus", new { ConversationId = conversationId, MessageId = messageId, Status = status });
    }
}
