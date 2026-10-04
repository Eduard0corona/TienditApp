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
        var confirmationLink = BuildEmailConfirmationLink(appUrl, userId, token);

        var subject = "Confirma tu correo - TienditApp";
        var body = $@"
            <h2>Bienvenido a TienditApp</h2>
            <p>Confirma tu correo con el siguiente enlace:</p>
            <p><a href='{confirmationLink}'>Confirmar correo</a></p>
            <p>Si no creaste una cuenta, puedes ignorar este mensaje.</p>
        ";

        await SendEmailAsync(email, subject, body);
    }

    // The app page confirms through the API and explains the result; linking the API itself showed raw JSON.
    internal static string BuildEmailConfirmationLink(string appUrl, string userId, string token)
        => $"{appUrl.TrimEnd('/')}/confirm-email?userId={Uri.EscapeDataString(userId)}&token={Uri.EscapeDataString(token)}";

    public async Task SendTeamInvitationAsync(string email, string businessName, string invitationPath)
    {
        var appUrl = _configuration["AppUrl"] ?? "https://localhost:5001";
        var link = $"{appUrl.TrimEnd('/')}{invitationPath}";
        // The name is typed by the business: one line in the subject, encoded in the body.
        var businessLine = businessName.ReplaceLineEndings(" ").Trim();
        var business = System.Net.WebUtility.HtmlEncode(businessLine);

        var subject = $"Te invitaron al equipo de {businessLine} - TienditApp";
        var body = $@"
            <h2>Te invitaron al equipo de {business}</h2>
            <p>Elige tu contraseña con el siguiente enlace para entrar a TienditApp (vale 7 días):</p>
            <p><a href='{link}'>Aceptar invitación</a></p>
            <p>Si no esperabas esta invitación, puedes ignorar este mensaje.</p>
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
