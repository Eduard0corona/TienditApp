namespace FoodParkDigital.Domain.Quotes;

/// <summary>
/// Where a version of a quote is (Fase 29C, ADR 0053 point 15). Only the aggregate changes it: a draft is sent, and a sent
/// one is accepted, rejected or replaced by a corrected version. <see cref="Expired"/> is never stored: a sent quote reads
/// expired once its validity passed (<see cref="Entities.Quote.StatusOn"/>).
/// </summary>
public enum QuoteStatus
{
    /// <summary>The business is still writing it: only it sees it.</summary>
    Draft = 0,
    /// <summary>The customer has its link and it is still valid.</summary>
    Sent = 1,
    Accepted = 2,
    Rejected = 3,
    /// <summary>Sent and its validity passed. Read only, never stored.</summary>
    Expired = 4,
    /// <summary>A corrected version was sent instead: it keeps what it said.</summary>
    Superseded = 5,
    /// <summary>A draft the business dropped before sending it. Kept, so its folio is never given again.</summary>
    Discarded = 6
}

/// <summary>
/// How the customer answered the quote: as the business records it, or <see cref="Link"/> when the customer answered from
/// the quote's link (Fase 29G, ADR 0061).
/// </summary>
public enum QuoteReplyChannel
{
    WhatsApp = 0,
    Email = 1,
    Phone = 2,
    InPerson = 3,
    /// <summary>The customer answered from the quote's link. Only they do: the business never records it by hand.</summary>
    Link = 4
}
