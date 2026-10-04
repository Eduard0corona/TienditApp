using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.WhatsApp.Enums;

namespace FoodParkDigital.Domain.WhatsApp.Entities;

public class WhatsAppConversation : AuditableEntity, ITenantScoped
{
    /// <summary>How long automatic replies stay quiet after a person of the team writes in the chat.</summary>
    public static readonly TimeSpan HumanReplyPause = TimeSpan.FromHours(12);

    private readonly List<WhatsAppMessage> _messages = new();

    private WhatsAppConversation() { }

    public WhatsAppConversation(Guid tenantId, string customerPhone, string? customerName = null)
    {
        TenantId = tenantId;
        CustomerPhone = customerPhone;
        CustomerName = customerName;
        LastMessageAt = DateTimeOffset.UtcNow;
    }

    public Guid TenantId { get; private set; }
    public string CustomerPhone { get; private set; } = null!;
    public string? CustomerName { get; private set; }
    public string? LastMessageBody { get; private set; }
    public DateTimeOffset LastMessageAt { get; private set; }
    public int UnreadCount { get; private set; }
    public bool IsArchived { get; private set; }
    /// <summary>Automatic replies stay quiet until then, so they never answer over a person of the team.</summary>
    public DateTimeOffset? AutoRepliesPausedUntil { get; private set; }

    public IReadOnlyCollection<WhatsAppMessage> Messages => _messages.AsReadOnly();

    public WhatsAppMessage AddMessage(MessageDirection direction, string body, string? externalMessageId)
    {
        var message = new WhatsAppMessage(Id, direction, body, externalMessageId);
        _messages.Add(message);

        LastMessageBody = body.Length > 500 ? body[..500] : body;
        LastMessageAt = message.SentAt;

        if (direction == MessageDirection.Inbound)
            UnreadCount++;

        return message;
    }

    public void MarkAsRead()
    {
        UnreadCount = 0;
    }

    /// <summary>A person took the conversation (or paused the replies by hand): the bot stays out until then.</summary>
    public void PauseAutoReplies(DateTimeOffset until) => AutoRepliesPausedUntil = until;

    public void ResumeAutoReplies() => AutoRepliesPausedUntil = null;

    public bool AutoRepliesPausedAt(DateTimeOffset now) => AutoRepliesPausedUntil is { } until && until > now;

    public void UpdateCustomerName(string? name)
    {
        if (!string.IsNullOrEmpty(name))
            CustomerName = name;
    }
}
