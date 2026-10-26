namespace FoodParkDigital.Application.Common.Interfaces;

/// <summary>
/// Sends the emails of the accounts. The emails of a request go out from the outbox
/// (<c>AccountEmailRequestedHandler</c>), never inside the request: a failure throws, so the outbox retries; without a
/// provider configured, or when the provider rejects the address for good, the email is logged as not sent and nothing
/// is thrown.
/// </summary>
public interface IEmailService
{
    Task SendEmailConfirmationAsync(string email, string userId, string token);
    Task SendPasswordResetEmailAsync(string email, string userId, string token);
    Task SendEmailAsync(string to, string subject, string body);

    /// <summary>The invitation to a business's team; <paramref name="invitationPath"/> is relative to the app.</summary>
    Task SendTeamInvitationAsync(string email, string businessName, string invitationPath);

    /// <summary>Someone signed up with this email, which already has a confirmed account: how to sign in.</summary>
    /// <param name="customerAccount">The account is a customer's, which cannot open a business.</param>
    Task SendAccountAlreadyExistsAsync(string email, bool customerAccount);

    /// <summary>
    /// Someone signed up with this email, which has an account started earlier and never activated: that sign-up was
    /// not saved, and the link activates the account started then, with the password chosen then.
    /// </summary>
    Task SendUnfinishedAccountAsync(string email, string userId, string token);

    /// <summary>
    /// An email of a business with files attached (the XML and PDF of a CFDI, Fase 28E). Unlike the others, it tells how it
    /// ended: sent, not sent because no provider is configured, or refused for good by the recipient's server. Anything
    /// that may work later throws, for the outbox to retry.
    /// </summary>
    Task<EmailSendOutcome> SendDocumentsAsync(DocumentEmail email, CancellationToken cancellationToken);
}

/// <param name="SenderName">The name the recipient sees as sender, such as «Café Luna vía TienditApp».</param>
/// <param name="ReplyTo">Where the recipient's answer goes: the business, not the platform; null when it has none.</param>
public sealed record DocumentEmail(
    string To,
    string Subject,
    string HtmlBody,
    string SenderName,
    string? ReplyTo,
    IReadOnlyList<EmailAttachment> Attachments);

public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content);

public enum EmailSendOutcome
{
    Sent,

    /// <summary>The platform has no provider configured: logged as not sent.</summary>
    NotConfigured,

    /// <summary>The recipient's server refused the address for good (5xx), or it is not an address: retrying cannot fix it.</summary>
    Rejected
}
