using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Tenants.Events;

/// <summary>
/// Someone of the team changed the notices by email of another person of the business (Fase 30A, ADR 0081): the person gets an
/// email that says who and what. Null for a notice that did not change; a person who changes their own is not told.
/// </summary>
/// <param name="TenantUserId">The person's membership in the business.</param>
/// <param name="ChangedByUserId">Whoever changed them, to say who in the email.</param>
/// <param name="RequestEmails">The agenda's notices (each request online and each cancellation), now on or off.</param>
/// <param name="QuoteReplyEmails">The answers to quotes, now on or off.</param>
/// <param name="OrderCancellationEmails">
/// The online orders their customers cancel (Fase 30B) and, since the Fase 31K, those still waiting to be accepted, now on or
/// off; missing in the messages stored before.
/// </param>
/// <param name="ReviewEmails">The new reviews (Fase 31K, ADR 0091), now on or off; missing in the messages stored before.</param>
public sealed record TenantUserNoticeEmailsChangedEvent(
    Guid TenantUserId, Guid ChangedByUserId, bool? RequestEmails, bool? QuoteReplyEmails, bool? OrderCancellationEmails = null,
    bool? ReviewEmails = null)
    : DomainEvent;
