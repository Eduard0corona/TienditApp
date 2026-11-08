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
public sealed record TenantUserNoticeEmailsChangedEvent(
    Guid TenantUserId, Guid ChangedByUserId, bool? RequestEmails, bool? QuoteReplyEmails) : DomainEvent;
