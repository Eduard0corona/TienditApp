using FoodParkDigital.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Infrastructure.Services;

public class EmailService : IEmailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendEmailConfirmationAsync(string email, string userId, string token)
    {
        var appUrl = _configuration["AppUrl"] ?? "https://localhost:5001";
        var confirmationLink = $"{appUrl}/api/v1/auth/confirm-email?userId={userId}&token={Uri.EscapeDataString(token)}";

        var subject = "Confirma tu email - FoodPark Digital";
        var body = $@"
            <h2>Bienvenido a FoodPark Digital</h2>
            <p>Por favor confirma tu email haciendo clic en el siguiente enlace:</p>
            <p><a href='{confirmationLink}'>Confirmar Email</a></p>
            <p>Si no creaste una cuenta, puedes ignorar este mensaje.</p>
        ";

        await SendEmailAsync(email, subject, body);
    }

    public async Task SendPasswordResetEmailAsync(string email, string userId, string token)
    {
        var appUrl = _configuration["AppUrl"] ?? "https://localhost:5001";
        var resetLink = $"{appUrl}/reset-password?userId={userId}&token={Uri.EscapeDataString(token)}";

        var subject = "Restablecer contraseña - FoodPark Digital";
        var body = $@"
            <h2>Restablecer Contraseña</h2>
            <p>Has solicitado restablecer tu contraseña. Haz clic en el siguiente enlace:</p>
            <p><a href='{resetLink}'>Restablecer Contraseña</a></p>
            <p>Si no solicitaste esto, puedes ignorar este mensaje.</p>
        ";

        await SendEmailAsync(email, subject, body);
    }

    public Task SendEmailAsync(string to, string subject, string body)
    {
        // TODO: Integrar con un servicio de email real (SendGrid, Amazon SES, MailKit/SMTP)
        _logger.LogInformation("Enviando email a {Email} con asunto: {Subject}", to, subject);
        _logger.LogDebug("Cuerpo del email: {Body}", body);
        _logger.LogInformation("Email enviado exitosamente a {Email}", to);

        return Task.CompletedTask;
    }
}
