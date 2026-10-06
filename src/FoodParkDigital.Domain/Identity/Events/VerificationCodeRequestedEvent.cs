using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Identity.Events;

/// <summary>
/// Someone without a session asked for a verification code by SMS. It is raised before knowing whether the account
/// exists, so the request does the same work either way, and the code goes out after the answer, from the outbox.
/// </summary>
/// <param name="Email">The account's email, for <see cref="VerificationCodePurpose.PasswordReset"/>.</param>
/// <param name="UserId">The account, for <see cref="VerificationCodePurpose.PhoneConfirmation"/> from the screen that
/// received it at sign-up.</param>
/// <param name="PhoneNumber">The phone as typed, for <see cref="VerificationCodePurpose.PhoneConfirmation"/> without
/// the account.</param>
public sealed record VerificationCodeRequestedEvent(
    VerificationCodePurpose Purpose,
    string? Email = null,
    Guid? UserId = null,
    string? PhoneNumber = null) : DomainEvent;

/// <summary>What the code is for. Stored in the outbox as a number: keep the values.</summary>
public enum VerificationCodePurpose
{
    /// <summary>Recovering the password with a code sent to the phone the account confirmed.</summary>
    PasswordReset = 0,

    /// <summary>Confirming the phone of an account that has not confirmed it yet.</summary>
    PhoneConfirmation = 1
}
