namespace FoodParkDigital.Application.Common.Interfaces;

public interface IEmailService
{
    Task SendEmailConfirmationAsync(string email, string userId, string token);
    Task SendPasswordResetEmailAsync(string email, string userId, string token);
    Task SendEmailAsync(string to, string subject, string body);
}
