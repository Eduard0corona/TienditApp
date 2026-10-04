using FoodParkDigital.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Mail;

namespace FoodParkDigital.Infrastructure.Services;

public class EmailService : IEmailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailService> _logger;
    private readonly bool _logSensitiveData;

    public EmailService(IConfiguration configuration, IHostEnvironment environment, ILogger<EmailService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        // Full addresses and SMTP server responses (which can echo them) are only logged in Development.
        _logSensitiveData = environment.IsDevelopment();
    }

    public async Task SendEmailConfirmationAsync(string email, string userId, string token)
    {
        var appUrl = _configuration["AppUrl"] ?? "https://localhost:5001";
        var confirmationLink = $"{appUrl}/api/v1/auth/confirm-email?userId={userId}&token={Uri.EscapeDataString(token)}";

        var subject = "Confirma tu correo - TienditApp";
        var body = $@"
            <h2>Bienvenido a TienditApp</h2>
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

        var subject = "Restablecer contraseña - TienditApp";
        var body = $@"
            <h2>Restablecer Contraseña</h2>
            <p>Has solicitado restablecer tu contraseña. Haz clic en el siguiente enlace:</p>
            <p><a href='{resetLink}'>Restablecer Contraseña</a></p>
            <p>Si no solicitaste esto, puedes ignorar este mensaje.</p>
        ";

        await SendEmailAsync(email, subject, body);
    }

    public async Task SendEmailAsync(string to, string subject, string body)
    {
        var provider = _configuration["EmailSettings:Provider"]?.Trim();
        if (!string.Equals(provider, "Smtp", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Email provider not configured for runtime sending. Email to {Email} was not sent.", LoggableEmail(to));
            _logger.LogInformation("Email subject: {Subject}", subject);
            return;
        }

        var host = _configuration["EmailSettings:Smtp:Host"];
        var portRaw = _configuration["EmailSettings:Smtp:Port"];
        var username = _configuration["EmailSettings:Smtp:Username"];
        var password = _configuration["EmailSettings:Smtp:Password"];
        var fromEmail = _configuration["EmailSettings:FromEmail"];
        var fromName = _configuration["EmailSettings:FromName"] ?? "TienditApp";
        var enableSsl = bool.TryParse(_configuration["EmailSettings:Smtp:EnableSsl"], out var ssl) && ssl;

        if (string.IsNullOrWhiteSpace(host)
            || string.IsNullOrWhiteSpace(portRaw)
            || !int.TryParse(portRaw, out var port)
            || string.IsNullOrWhiteSpace(fromEmail))
        {
            _logger.LogWarning("SMTP settings are incomplete. Email to {Email} was not sent.", LoggableEmail(to));
            return;
        }

        using var smtp = new SmtpClient(host, port)
        {
            EnableSsl = enableSsl
        };

        if (!string.IsNullOrWhiteSpace(username))
            smtp.Credentials = new NetworkCredential(username, password);

        using var mail = new MailMessage
        {
            From = new MailAddress(fromEmail, fromName),
            Subject = subject,
            Body = body,
            IsBodyHtml = true
        };
        mail.To.Add(to);

        try
        {
            await smtp.SendMailAsync(mail);
            _logger.LogInformation("Email sent successfully to {Email}", LoggableEmail(to));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                _logSensitiveData ? ex : null,
                "SMTP send failed for {Email}. Error: {ErrorType}. SMTP status: {SmtpStatusCode}",
                LoggableEmail(to),
                ex.GetType().Name,
                (ex as SmtpException)?.StatusCode);
        }
    }

    private string LoggableEmail(string email)
        => _logSensitiveData ? email : LogRedaction.MaskEmail(email);
}
