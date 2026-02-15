namespace FoodParkDigital.Application.WhatsApp.DTOs;

public class ChatMessageDto
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public string Direction { get; set; } = null!;
    public string Body { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTimeOffset SentAt { get; set; }
}
