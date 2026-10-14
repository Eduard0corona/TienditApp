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

    [Fact]
    public async Task SendEmailAsync_WhenSmtpRejectsForNow_ThrowsForTheOutboxToRetry_WithoutTheAddress()
    {
        await using var smtpServer = new RejectingSmtpServer("451 4.3.0");
        var logger = new CapturingLogger<EmailService>();
        var service = CreateService(Environments.Production, SmtpSettings(smtpServer.Port), logger);

        var thrown = await Assert.ThrowsAsync<EmailDeliveryException>(
            () => service.SendPasswordResetEmailAsync(Email, Guid.NewGuid().ToString(), Token));

        // The outbox stores and logs the message of what was thrown: no address, no answer of the server.
        Assert.DoesNotContain(Mailbox, thrown.Message);
        Assert.DoesNotContain("Recipient address rejected", thrown.Message);
        Assert.Null(thrown.InnerException);
        Assert.Equal(SmtpStatusCode.LocalErrorInProcessing, thrown.Status);
        Assert.Null(logger.FirstMentioning(Mailbox));
        Assert.Null(logger.FirstMentioning(Token));
    }

    [Fact]
    public async Task SendEmailAsync_WhenTheServerCannotBeReached_ThrowsForTheOutboxToRetry()
    {
        // A port nobody listens on: the connection is refused.
        var port = RejectingSmtpServerPortThatIsClosed();
        var service = CreateService(Environments.Production, SmtpSettings(port), new CapturingLogger<EmailService>());

        var thrown = await Assert.ThrowsAsync<EmailDeliveryException>(
            () => service.SendEmailConfirmationAsync(Email, Guid.NewGuid().ToString(), Token));
        Assert.DoesNotContain(Mailbox, thrown.Message);
    }

    [Theory]
    [InlineData("EmailSettings:Smtp:Username", "YOUR_SMTP_USERNAME")]
    [InlineData("EmailSettings:Smtp:Password", "YOUR_SMTP_PASSWORD")]
    [InlineData("EmailSettings:FromEmail", "noreply@foodparkdigital.local")]
    [InlineData("EmailSettings:FromEmail", "no es un correo")]
    [InlineData("EmailSettings:Smtp:Host", "")]
    public async Task SendEmailAsync_WithTheExampleOrIncompleteSettings_DoesNotTryToSend(string setting, string value)
    {
        await using var smtpServer = new RejectingSmtpServer();
        var logger = new CapturingLogger<EmailService>();
        var settings = SmtpSettings(smtpServer.Port);
        settings[setting] = value;
        var service = CreateService(Environments.Production, settings, logger);

        await service.SendEmailConfirmationAsync(Email, Guid.NewGuid().ToString(), Token);

        Assert.Empty(smtpServer.Commands);
        var warning = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains(MaskedEmail, warning.Message);
        Assert.Null(logger.FirstMentioning(Token));
    }

    [Fact]
    public async Task SendEmailAsync_WithoutAProviderInDevelopment_LogsTheEmailToOpenItsLinkLocally()
    {
        var logger = new CapturingLogger<EmailService>();
        var service = CreateService(Environments.Development, new() { ["EmailSettings:Provider"] = "" }, logger);

        await service.SendEmailConfirmationAsync(Email, "user-1", Token);

        Assert.NotNull(logger.FirstMentioning(Token));
    }

    [Fact]
    public async Task SendAccountAlreadyExistsAsync_SaysHowToSignIn_AndThatNothingChanged()
    {
        var logger = new CapturingLogger<EmailService>();
        var service = CreateService(
            Environments.Development,
            new() { ["EmailSettings:Provider"] = "", ["AppUrl"] = "https://app.tienditapp.mx/" },
            logger);

        await service.SendAccountAlreadyExistsAsync(Email);

        Assert.NotNull(logger.FirstMentioning("Ya tienes una cuenta - TienditApp"));
        Assert.NotNull(logger.FirstMentioning("https://app.tienditapp.mx/login"));
        Assert.NotNull(logger.FirstMentioning("https://app.tienditapp.mx/forgot-password"));
        Assert.NotNull(logger.FirstMentioning("tu cuenta no cambió"));
    }

    private static int RejectingSmtpServerPortThatIsClosed()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
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
