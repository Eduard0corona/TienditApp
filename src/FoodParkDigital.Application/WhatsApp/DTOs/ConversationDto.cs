namespace FoodParkDigital.Application.WhatsApp.DTOs;

public class ConversationDto
{
    public Guid Id { get; set; }
    public string CustomerPhone { get; set; } = null!;
    public string? CustomerName { get; set; }
    public string? LastMessageBody { get; set; }
    public DateTimeOffset LastMessageAt { get; set; }
    public int UnreadCount { get; set; }
}
