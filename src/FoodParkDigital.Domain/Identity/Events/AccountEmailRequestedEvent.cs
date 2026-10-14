using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Identity.Events;

/// <summary>
/// A request asked for an email of an account: the link that confirms its email, the one that resets its password,
/// the notice that a sign-up used an email that already has an account, or a team's invitation. It is stored in the
/// outbox with the change of the request and the email goes out after the answer, so the answer neither waits for the
/// provider nor tells, by its time, whether the account exists. The link is created when the email is sent: the
/// outbox never stores one.
/// </summary>
/// <param name="Email">The email as typed, when the request does not know the account (a reset, a new link, a sign-up
/// with an email that has one).</param>
/// <param name="UserId">The account, when the request created or found it (a sign-up, an invitation).</param>
public sealed record AccountEmailRequestedEvent(
    AccountEmailPurpose Purpose,
    string? Email = null,
    Guid? UserId = null) : DomainEvent;

/// <summary>What the email is for. Stored in the outbox as a number: keep the values.</summary>
public enum AccountEmailPurpose
{
    /// <summary>The link that confirms the email and activates the account; only while it is not confirmed.</summary>
    EmailConfirmation = 0,

    /// <summary>The link that resets the password; only for an account that confirmed its email.</summary>
    PasswordReset = 1,

    /// <summary>
    /// Someone signed up with an email that already has an account: an account not confirmed yet gets its
    /// confirmation link again, a confirmed one the notice that it already exists, with how to sign in.
    /// </summary>
    SignUpWithExistingEmail = 2,

    /// <summary>The invitation to the team of the business the event belongs to, while it is pending.</summary>
    TeamInvitation = 3
}
