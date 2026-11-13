namespace FoodParkDigital.Application.WhatsApp.DTOs;

public class ChatMessageDto
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public string Direction { get; set; } = null!;
    public string Body { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTimeOffset SentAt { get; set; }
    /// <summary>"Image" or "Audio" when the customer sent a photo or an audio the chat shows (Fase 31L); null otherwise.</summary>
    public string? MediaKind { get; set; }
    /// <summary>"Pending" while it downloads, "Saved" once kept, "Unavailable" when only its marker stays.</summary>
    public string? MediaState { get; set; }
    /// <summary>The private file path ("/files/whatsapp/..."), opened with the session, once kept.</summary>
    public string? MediaPath { get; set; }
}
