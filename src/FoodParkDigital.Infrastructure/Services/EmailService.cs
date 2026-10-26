using FoodParkDigital.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Mail;

namespace FoodParkDigital.Infrastructure.Services;

/// <summary>
/// Sends the emails of the accounts, and those with the CFDI of a business (Fase 28E), by SMTP
/// (<see cref="EmailDeliverySettings"/>). Full addresses and the answers of the SMTP server, which can echo them, are only
/// logged in Development.
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

    public async Task SendAccountAlreadyExistsAsync(string email, bool customerAccount)
    {
        var appUrl = (_configuration["AppUrl"] ?? "https://localhost:5001").TrimEnd('/');
        var business = customerAccount
            ? "<p>Es una cuenta de cliente: para registrar un negocio, usa otro correo.</p>"
            : string.Empty;

        var subject = "Ya tienes una cuenta - TienditApp";
        var body = $@"
            <h2>Ya tienes una cuenta en TienditApp</h2>
            <p>Alguien intentó crear una cuenta con este correo, pero ya tiene una. Si fuiste tú, entra con tu correo y tu contraseña:</p>
            <p><a href='{appUrl}/login'>Iniciar sesión</a></p>
            <p>¿No recuerdas tu contraseña? <a href='{appUrl}/forgot-password'>Recupérala aquí</a>.</p>
            {business}
            <p>Si no fuiste tú, puedes ignorar este mensaje: tu cuenta no cambió.</p>
        ";

        await SendEmailAsync(email, subject, body);
    }

    public async Task SendUnfinishedAccountAsync(string email, string userId, string token)
    {
        var appUrl = _configuration["AppUrl"] ?? "https://localhost:5001";
        var link = BuildEmailConfirmationLink(appUrl, userId, token);

        var subject = "Ya empezaste una cuenta - TienditApp";
        var body = $@"
            <h2>Ya hay una cuenta empezada con este correo</h2>
            <p>Alguien intentó crear una cuenta en TienditApp con este correo, pero ya hay una que no se ha activado. Lo que se llenó esta vez no se guardó.</p>
            <p>Si la empezaste tú, actívala con este enlace y entra con la contraseña que elegiste esa vez:</p>
            <p><a href='{link}'>Activar mi cuenta</a></p>
            <p>¿No recuerdas esa contraseña? Después de activarla, recupérala desde «¿Olvidaste tu contraseña?».</p>
            <p>Si no la empezaste tú, no abras el enlace: una cuenta sin activar no puede entrar.</p>
        ";

        await SendEmailAsync(email, subject, body);
    }

    /// <summary>
    /// Sends the email. Without a provider configured it is logged as not sent; a 5xx answer to the recipient (a
    /// mailbox that does not exist, an address the server refuses) is logged as an error: retrying cannot fix the
    /// address. Anything else throws <see cref="EmailDeliveryException"/>, without the address, so the outbox retries:
    /// a 4xx answer, no connection, a timeout, and also the failures of the server or of its settings (wrong or rotated
    /// credentials, a refused sender or relay), which are fixed for every email at once. Those that keep failing are set
    /// aside after the last attempt and the background queues show Degraded.
    /// </summary>
    public Task SendEmailAsync(string to, string subject, string body)
        => DeliverAsync(to, subject, body, senderName: null, replyTo: null, attachments: [], CancellationToken.None);

    /// <inheritdoc />
    public Task<EmailSendOutcome> SendDocumentsAsync(DocumentEmail email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);
        return DeliverAsync(email.To, email.Subject, email.HtmlBody, email.SenderName, email.ReplyTo, email.Attachments, cancellationToken);
    }

    private async Task<EmailSendOutcome> DeliverAsync(
        string to,
        string subject,
        string body,
        string? senderName,
        string? replyTo,
        IReadOnlyList<EmailAttachment> attachments,
        CancellationToken cancellationToken)
    {
        var (settings, problem) = EmailDeliverySettings.Read(_configuration);
        if (settings is null)
        {
            _logger.LogWarning("Email not sent to {Email}: {Problem}", LoggableEmail(to), problem);
            _logger.LogInformation("Email subject: {Subject}", subject);
            // Its link is the only way to test sign-ups locally without a provider.
            if (_logSensitiveData)
                _logger.LogInformation("Email body (Development only): {Body}", body);
            return EmailSendOutcome.NotConfigured;
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
            return EmailSendOutcome.Rejected;
        }

        using var mail = new MailMessage
        {
            // The platform always sends (its domain is the one the provider trusts); a business only names itself.
            From = new MailAddress(settings.FromEmail, SenderName(senderName) ?? settings.FromName),
            Subject = subject,
            Body = body,
            IsBodyHtml = true
        };
        mail.To.Add(recipient);
        if (replyTo is not null && MailAddress.TryCreate(replyTo, out var answerTo))
            mail.ReplyToList.Add(answerTo);
        foreach (var file in attachments)
        {
            var attachment = new Attachment(new MemoryStream(file.Content), file.FileName, file.ContentType);
            // Some mail apps only read the name from here.
            attachment.ContentDisposition!.FileName = file.FileName;
            mail.Attachments.Add(attachment);
        }

        try
        {
            await smtp.SendMailAsync(mail, cancellationToken);
            _logger.LogInformation("Email sent successfully to {Email}", LoggableEmail(to));
            return EmailSendOutcome.Sent;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var status = (ex as SmtpException)?.StatusCode;
            // Only the recipient is rejected for good; a 5xx to the sender or the login is a setting to fix.
            var permanent = ex is SmtpFailedRecipientException && status is { } code && (int)code >= 500;
            _logger.LogError(
                _logSensitiveData ? ex : null,
                "SMTP send failed for {Email}. Error: {ErrorType}. SMTP status: {SmtpStatusCode}. {Outcome}",
                LoggableEmail(to),
                ex.GetType().Name,
                status,
                permanent ? "Rejected for good: not retried." : "It will be retried.");
            if (!permanent)
                throw new EmailDeliveryException(status);
            return EmailSendOutcome.Rejected;
        }
    }

    // A name typed by the business: one line, without what would break the header.
    private static string? SenderName(string? name)
    {
        var line = name?.ReplaceLineEndings(" ").Replace("\"", string.Empty).Trim();
        return string.IsNullOrEmpty(line) ? null : line;
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
