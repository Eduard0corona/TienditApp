using System.Linq.Expressions;
using FoodParkDigital.Domain.WhatsApp.Entities;

namespace FoodParkDigital.Application.WhatsApp.DTOs;

/// <summary>Conversations and messages of the chat; the same mapping serves SQL lists and loaded entities.</summary>
public static class WhatsAppMappings
{
    public static readonly Expression<Func<WhatsAppConversation, ConversationDto>> ConversationAsDto = conversation => new ConversationDto
    {
        Id = conversation.Id,
        CustomerPhone = conversation.CustomerPhone,
        CustomerName = conversation.CustomerName,
        LastMessageBody = conversation.LastMessageBody,
        LastMessageAt = conversation.LastMessageAt,
        UnreadCount = conversation.UnreadCount,
        AutoRepliesPausedUntil = conversation.AutoRepliesPausedUntil
    };

    public static readonly Expression<Func<WhatsAppMessage, ChatMessageDto>> MessageAsDto = message => new ChatMessageDto
    {
        Id = message.Id,
        ConversationId = message.ConversationId,
        Direction = message.Direction.ToString(),
        Body = message.Body,
        Status = message.Status.ToString(),
        SentAt = message.SentAt
    };

    private static readonly Func<WhatsAppConversation, ConversationDto> ConversationCompiled = ConversationAsDto.Compile();
    private static readonly Func<WhatsAppMessage, ChatMessageDto> MessageCompiled = MessageAsDto.Compile();

    public static ConversationDto ToDto(this WhatsAppConversation conversation) => ConversationCompiled(conversation);

    public static ChatMessageDto ToDto(this WhatsAppMessage message) => MessageCompiled(message);
}
