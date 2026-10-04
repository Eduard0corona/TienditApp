using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Messaging.Enums;

namespace FoodParkDigital.Domain.Messaging.Entities;

/// <summary>
/// A customer who asked not to receive campaigns or reminders. Writing ALTA ends it without deleting it, so
/// the business keeps when and how the customer chose.
/// </summary>
public class MessagingOptOut : AuditableEntity, ITenantScoped
{
    private MessagingOptOut() { }

    public MessagingOptOut(Guid tenantId, string phone, OptOutSource source, DateTimeOffset optedOutAt)
    {
        TenantId = tenantId;
        Phone = MessagingPhone.ToKey(phone);
        Source = source;
        OptedOutAt = optedOutAt;
    }

    public Guid TenantId { get; private set; }
    /// <summary>The recipient key (<see cref="MessagingPhone.ToKey"/>).</summary>
    public string Phone { get; private set; } = null!;
    public OptOutSource Source { get; private set; }
    public DateTimeOffset OptedOutAt { get; private set; }
    public DateTimeOffset? ResubscribedAt { get; private set; }

    public bool IsActive => ResubscribedAt is null;

    public void OptOut(OptOutSource source, DateTimeOffset optedOutAt)
    {
        if (IsActive)
            return;

        Source = source;
        OptedOutAt = optedOutAt;
        ResubscribedAt = null;
    }

    public void Resubscribe(DateTimeOffset resubscribedAt) => ResubscribedAt ??= resubscribedAt;
}
