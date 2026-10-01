using System.Net.Mail;
using FoodParkDigital.Infrastructure.Services;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Infrastructure.Tests;

public class EmailServiceLoggingTests
{
    private const string Email = "ana.cliente@example.com";
    private const string Mailbox = "ana.cliente";
    private const string MaskedEmail = "a***@e***.com";
    private const string Token = "CfDJ8-reset-token-secret";

    [Fact]
    public async Task SendEmailConfirmationAsync_WithoutEmailProvider_LogsOnlyMaskedRecipientOutsideDevelopment()
    {
        var logger = new CapturingLogger<EmailService>();
        var service = CreateService(Environments.Production, new() { ["EmailSettings:Provider"] = "" }, logger);

        await service.SendEmailConfirmationAsync(Email, Guid.NewGuid().ToString(), Token);

        Assert.Null(logger.FirstMentioning(Mailbox));
        Assert.Null(logger.FirstMentioning(Token));
        var warning = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains(MaskedEmail, warning.Message);
    }

    [Fact]
    public async Task SendPasswordResetEmailAsync_WhenSmtpRejectsRecipient_LogsOnlyErrorTypeOutsideDevelopment()
    {
        await using var smtpServer = new RejectingSmtpServer();
        var logger = new CapturingLogger<EmailService>();
        var service = CreateService(Environments.Production, SmtpSettings(smtpServer.Port), logger);

        await service.SendPasswordResetEmailAsync(Email, Guid.NewGuid().ToString(), Token);

        Assert.Contains(smtpServer.Commands, command => command.Contains($"<{Email}>"));
        Assert.Null(logger.FirstMentioning(Mailbox));
        Assert.Null(logger.FirstMentioning(Token));
        Assert.Null(logger.FirstMentioning("Recipient address rejected"));
        var error = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Null(error.Exception);
        Assert.Equal(nameof(SmtpFailedRecipientException), error["ErrorType"]);
        Assert.Equal(SmtpStatusCode.MailboxUnavailable, error["SmtpStatusCode"]);
        Assert.Contains(MaskedEmail, error.Message);
    }

    [Fact]
    public async Task SendPasswordResetEmailAsync_WhenSmtpRejectsRecipientInDevelopment_LogsExceptionForDebugging()
    {
        await using var smtpServer = new RejectingSmtpServer();
        var logger = new CapturingLogger<EmailService>();
        var service = CreateService(Environments.Development, SmtpSettings(smtpServer.Port), logger);

        await service.SendPasswordResetEmailAsync(Email, Guid.NewGuid().ToString(), Token);

        var error = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.IsType<SmtpFailedRecipientException>(error.Exception);
        Assert.Contains(Email, error.Message);
    }

    private static EmailService CreateService(
        string environmentName,
        Dictionary<string, string?> settings,
        CapturingLogger<EmailService> logger)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new EmailService(configuration, new TestHostEnvironment(environmentName), logger);
    }

    private static Dictionary<string, string?> SmtpSettings(int port) => new()
    {
        ["EmailSettings:Provider"] = "Smtp",
        ["EmailSettings:FromEmail"] = "noreply@example.com",
        ["EmailSettings:Smtp:Host"] = "127.0.0.1",
        ["EmailSettings:Smtp:Port"] = port.ToString(),
        ["EmailSettings:Smtp:EnableSsl"] = "false"
    };
}
