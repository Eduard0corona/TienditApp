namespace FoodParkDigital.Domain.Messaging.Enums;

/// <summary>What a template or a queued message is for; it decides which placeholders it may use.</summary>
public enum MessagePurpose
{
    Campaign = 0,
    AppointmentReminder = 1,
    /// <summary>
    /// An appointment was booked, moved, confirmed or cancelled. Only queued messages use it (no templates), and an
    /// opt-out does not stop it: like an order notice, the customer asked for that appointment.
    /// </summary>
    AppointmentNotice = 2,
    /// <summary>
    /// What a customer owes past the term of the fiado (Fase 26C, ADR 0035), queued by the system while it stays overdue. An
    /// opt-out stops it, as every reminder.
    /// </summary>
    CreditReminder = 3,
    /// <summary>
    /// What a customer who is up to date owes that comes due in a few days (Fase 26E, ADR 0043), queued by the system once per
    /// last day to pay. An opt-out stops it, as every reminder.
    /// </summary>
    CreditDueSoon = 4
}

public enum OutgoingMessageStatus
{
    Pending = 0,
    Sent = 1,
    /// <summary>Rejected by the provider or out of attempts.</summary>
    Failed = 2,
    /// <summary>
    /// Not sent on purpose: the customer opted out, the appointment changed or was cancelled, nothing is overdue anymore, or
    /// nothing comes due that day anymore.
    /// </summary>
    Skipped = 3,
    /// <summary>The campaign was cancelled before the message went out.</summary>
    Cancelled = 4
}

public enum CampaignStatus
{
    Scheduled = 0,
    Sending = 1,
    Completed = 2,
    Cancelled = 3
}

public enum CampaignAudienceKind
{
    /// <summary>Registered customers with a phone and everyone who ordered with one.</summary>
    AllCustomers = 0,
    /// <summary>Customers with at least N completed orders, optionally within the last days.</summary>
    Buyers = 1,
    /// <summary>Customers who bought before but not in the last days.</summary>
    Lapsed = 2
}

public enum OptOutSource
{
    /// <summary>The customer wrote BAJA (or STOP) in WhatsApp.</summary>
    CustomerKeyword = 0,
    /// <summary>Someone of the business recorded it at the customer's request.</summary>
    Staff = 1
}
