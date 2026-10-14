namespace FoodParkDigital.Application.Common.Interfaces;

/// <summary>
/// Sends the emails of the accounts. The emails of a request go out from the outbox
/// (<c>AccountEmailRequestedHandler</c>), never inside the request: a failure the provider may recover from throws, so
/// the outbox retries; without a provider configured, or when the provider rejects the address for good, the email is
/// logged as not sent and nothing is thrown.
/// </summary>
public interface IEmailService
{
    Task SendEmailConfirmationAsync(string email, string userId, string token);
    Task SendPasswordResetEmailAsync(string email, string userId, string token);
    Task SendEmailAsync(string to, string subject, string body);

    /// <summary>The invitation to a business's team; <paramref name="invitationPath"/> is relative to the app.</summary>
    Task SendTeamInvitationAsync(string email, string businessName, string invitationPath);

    /// <summary>Someone signed up with this email, which already has a confirmed account: how to sign in.</summary>
    Task SendAccountAlreadyExistsAsync(string email);
}
