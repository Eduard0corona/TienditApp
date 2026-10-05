namespace FoodParkDigital.Application.Common.Interfaces;

public interface IEmailService
{
    Task SendEmailConfirmationAsync(string email, string userId, string token);
    Task SendPasswordResetEmailAsync(string email, string userId, string token);
    Task SendEmailAsync(string to, string subject, string body);

    /// <summary>The invitation to a business's team; <paramref name="invitationPath"/> is relative to the app.</summary>
    Task SendTeamInvitationAsync(string email, string businessName, string invitationPath);
}
