using FoodParkDigital.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Mail;

namespace FoodParkDigital.Infrastructure.Services;

/// <summary>
/// Sends the emails of the accounts by SMTP (<see cref="EmailDeliverySettings"/>). Full addresses and the answers of the
/// SMTP server, which can echo them, are only logged in Development.
/// </summary>
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

    public async Task SendAccountAlreadyExistsAsync(string email)
    {
        var appUrl = (_configuration["AppUrl"] ?? "https://localhost:5001").TrimEnd('/');

        var subject = "Ya tienes una cuenta - TienditApp";
        var body = $@"
            <h2>Ya tienes una cuenta en TienditApp</h2>
            <p>Alguien intentó crear una cuenta con este correo, pero ya tiene una. Si fuiste tú, entra con tu correo y tu contraseña:</p>
            <p><a href='{appUrl}/login'>Iniciar sesión</a></p>
            <p>¿No recuerdas tu contraseña? <a href='{appUrl}/forgot-password'>Recupérala aquí</a>.</p>
            <p>Si no fuiste tú, puedes ignorar este mensaje: tu cuenta no cambió.</p>
        ";

        await SendEmailAsync(email, subject, body);
    }

    /// <summary>
    /// Sends the email. Without a provider configured it is logged as not sent; a rejection the provider gives for
    /// good (a 5xx answer, such as a mailbox that does not exist) is logged as an error. Anything else (a 4xx answer,
    /// no connection, a timeout) throws <see cref="EmailDeliveryException"/>, without the address, so the outbox
    /// retries.
    /// </summary>
    public async Task SendEmailAsync(string to, string subject, string body)
    {
        var (settings, problem) = EmailDeliverySettings.Read(_configuration);
        if (settings is null)
        {
            _logger.LogWarning("Email not sent to {Email}: {Problem}", LoggableEmail(to), problem);
            _logger.LogInformation("Email subject: {Subject}", subject);
            // Its link is the only way to test sign-ups locally without a provider.
            if (_logSensitiveData)
                _logger.LogInformation("Email body (Development only): {Body}", body);
            return;
        }

        using var smtp = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = settings.EnableSsl
        };

        if (settings.Username is not null)
            smtp.Credentials = new NetworkCredential(settings.Username, settings.Password);

        if (!MailAddress.TryCreate(to, out var recipient))
        {
            // Retrying cannot fix the address.
            _logger.LogWarning("Email not sent to {Email}: the address is not valid.", LoggableEmail(to));
            return;
        }

        using var mail = new MailMessage
        {
            From = new MailAddress(settings.FromEmail, settings.FromName),
            Subject = subject,
            Body = body,
            IsBodyHtml = true
        };
        mail.To.Add(recipient);

        try
        {
            await smtp.SendMailAsync(mail);
            _logger.LogInformation("Email sent successfully to {Email}", LoggableEmail(to));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var status = (ex as SmtpException)?.StatusCode;
            var permanent = status is { } code && (int)code >= 500;
            _logger.LogError(
                _logSensitiveData ? ex : null,
                "SMTP send failed for {Email}. Error: {ErrorType}. SMTP status: {SmtpStatusCode}. {Outcome}",
                LoggableEmail(to),
                ex.GetType().Name,
                status,
                permanent ? "Rejected for good: not retried." : "It will be retried.");
            if (!permanent)
                throw new EmailDeliveryException(status);
        }
    }

    private string LoggableEmail(string email)
        => _logSensitiveData ? email : LogRedaction.MaskEmail(email);
}

/// <summary>
/// The SMTP server did not take an email, for a reason it may recover from. It carries neither the address nor the
/// server's answer (which can echo it): the outbox stores and logs this message.
/// </summary>
public sealed class EmailDeliveryException : Exception
{
    public EmailDeliveryException(SmtpStatusCode? status)
        : base(status is { } code
            ? $"The SMTP server did not take the email ({(int)code} {code})."
            : "The SMTP server could not be reached.")
    {
        Status = status;
    }

    public SmtpStatusCode? Status { get; }
}
