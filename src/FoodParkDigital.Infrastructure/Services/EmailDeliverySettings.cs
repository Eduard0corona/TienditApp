using System.Net.Mail;
using Microsoft.Extensions.Configuration;

namespace FoodParkDigital.Infrastructure.Services;

/// <summary>
/// The SMTP settings of <c>EmailSettings</c>, read the same way by <see cref="EmailService"/> and its health check.
/// <c>appsettings.json</c> carries example values (a Mailtrap host, <c>YOUR_SMTP_USERNAME</c>): they count as not
/// configured, so nothing tries to send with them and the health check says so.
/// </summary>
public sealed record EmailDeliverySettings(
    string Host,
    int Port,
    string? Username,
    string? Password,
    bool EnableSsl,
    string FromEmail,
    string FromName)
{
    public const string ProviderSetting = "EmailSettings:Provider";

    /// <summary>The settings, or why emails cannot be sent (for the log and the health check, never for people).</summary>
    public static (EmailDeliverySettings? Settings, string? Problem) Read(IConfiguration configuration)
    {
        var provider = configuration[ProviderSetting]?.Trim();
        if (!string.Equals(provider, "Smtp", StringComparison.OrdinalIgnoreCase))
            return (null, "No email provider is configured (EmailSettings:Provider is not Smtp).");

        var host = configuration["EmailSettings:Smtp:Host"]?.Trim();
        var portRaw = configuration["EmailSettings:Smtp:Port"];
        var username = configuration["EmailSettings:Smtp:Username"];
        var password = configuration["EmailSettings:Smtp:Password"];
        var fromEmail = configuration["EmailSettings:FromEmail"]?.Trim();

        if (string.IsNullOrWhiteSpace(host)
            || !int.TryParse(portRaw, out var port)
            || port <= 0
            || string.IsNullOrWhiteSpace(fromEmail))
        {
            return (null, "The SMTP settings are incomplete (host, port or sender).");
        }

        if (IsExample(username) || IsExample(password))
            return (null, "The SMTP account still has the example values of appsettings.json.");

        if (!MailAddress.TryCreate(fromEmail, out _))
            return (null, "The sender (EmailSettings:FromEmail) is not a valid address.");

        // A .local sender is the example of appsettings.json: no provider delivers it.
        if (fromEmail.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            return (null, "The sender is the example address of appsettings.json.");

        return (new EmailDeliverySettings(
            host,
            port,
            string.IsNullOrWhiteSpace(username) ? null : username,
            password,
            bool.TryParse(configuration["EmailSettings:Smtp:EnableSsl"], out var ssl) && ssl,
            fromEmail,
            configuration["EmailSettings:FromName"] ?? "TienditApp"), null);
    }

    private static bool IsExample(string? value)
        => value is not null && value.Trim().StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase);
}
