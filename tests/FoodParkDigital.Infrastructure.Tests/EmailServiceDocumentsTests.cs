using System.Text;
using System.Text.RegularExpressions;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Infrastructure.Services;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// The emails of a business with files attached (the XML and PDF of a CFDI, Fase 28E): the platform sends them in the
/// business's name, answers go to the business, and how each ended is told to the outbox.
/// </summary>
public partial class EmailServiceDocumentsTests
{
    private const string Email = "ana.cliente@example.com";
    private const string Mailbox = "ana.cliente";

    private static readonly byte[] Xml = Encoding.UTF8.GetBytes("<cfdi:Comprobante Total=\"116.00\" />");
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7 factura");

    [Fact]
    public async Task SendDocumentsAsync_ShouldSendTheFiles_InTheBusinessesName_WithAnswersToTheBusiness()
    {
        await using var smtpServer = new AcceptingSmtpServer();
        var logger = new CapturingLogger<EmailService>();
        var service = CreateService(Environments.Production, SmtpSettings(smtpServer.Port), logger);

        var outcome = await service.SendDocumentsAsync(Message(senderName: "Café \"Luna\"\r\nvía TienditApp", replyTo: "hola@cafeluna.mx"),
            CancellationToken.None);

        Assert.Equal(EmailSendOutcome.Sent, outcome);
        var headers = Headers(smtpServer.Message);
        // The platform's address sends it (its domain is the one the provider trusts); the business only names itself, in one line.
        Assert.Equal("Café Luna vía TienditApp <noreply@example.com>", Decoded(headers["From"]));
        Assert.Equal("hola@cafeluna.mx", headers["Reply-To"]);
        Assert.Equal("Factura F-1 de Café Luna", Decoded(headers["Subject"]));
        var unwrapped = smtpServer.Message.Replace("\r\n", string.Empty);
        Assert.Contains("Content-Type: application/xml; name=F-1_UUID.xmlContent-Transfer-Encoding: base64Content-Disposition: attachment; filename=F-1_UUID.xml", unwrapped);
        Assert.Contains("Content-Type: application/pdf; name=F-1_UUID.pdfContent-Transfer-Encoding: base64Content-Disposition: attachment; filename=F-1_UUID.pdf", unwrapped);
        Assert.Contains(Convert.ToBase64String(Xml), unwrapped);
        Assert.Contains(Convert.ToBase64String(Pdf), unwrapped);
        // Outside Development, the address is logged masked.
        Assert.Null(logger.FirstMentioning(Mailbox));
        Assert.NotNull(logger.FirstMentioning("a***@e***.com"));
    }

    [Fact]
    public async Task SendDocumentsAsync_WithoutAValidReplyTo_ShouldStillSendIt_AsThePlatform()
    {
        await using var smtpServer = new AcceptingSmtpServer();
        var service = CreateService(Environments.Production, SmtpSettings(smtpServer.Port), new CapturingLogger<EmailService>());

        var outcome = await service.SendDocumentsAsync(Message(senderName: " ", replyTo: "no es un correo"), CancellationToken.None);

        Assert.Equal(EmailSendOutcome.Sent, outcome);
        var headers = Headers(smtpServer.Message);
        Assert.False(headers.ContainsKey("Reply-To"));
        Assert.Equal("TienditApp <noreply@example.com>", Decoded(headers["From"]));
    }

    [Fact]
    public async Task SendDocumentsAsync_ShouldTellHowItEnded_WithoutAProvider_OrRefusedForGood()
    {
        var withoutProvider = CreateService(Environments.Production, new() { ["EmailSettings:Provider"] = "" }, new CapturingLogger<EmailService>());
        Assert.Equal(EmailSendOutcome.NotConfigured, await withoutProvider.SendDocumentsAsync(Message(), CancellationToken.None));

        await using (var smtpServer = new RejectingSmtpServer())
        {
            var service = CreateService(Environments.Production, SmtpSettings(smtpServer.Port), new CapturingLogger<EmailService>());
            Assert.Equal(EmailSendOutcome.Rejected, await service.SendDocumentsAsync(Message(), CancellationToken.None));
        }

        // Not an address: nothing to connect for.
        var notAnAddress = CreateService(Environments.Production, SmtpSettings(1), new CapturingLogger<EmailService>());
        Assert.Equal(EmailSendOutcome.Rejected, await notAnAddress.SendDocumentsAsync(Message(to: "ana@"), CancellationToken.None));
    }

    [Fact]
    public async Task SendDocumentsAsync_WhenTheServerPutsItOff_ThrowsForTheOutboxToRetry_WithoutTheAddress()
    {
        await using var smtpServer = new RejectingSmtpServer("451 4.3.0");
        var service = CreateService(Environments.Production, SmtpSettings(smtpServer.Port), new CapturingLogger<EmailService>());

        var thrown = await Assert.ThrowsAsync<EmailDeliveryException>(() => service.SendDocumentsAsync(Message(), CancellationToken.None));

        Assert.DoesNotContain(Mailbox, thrown.Message);
    }

    private static DocumentEmail Message(string to = Email, string senderName = "Café Luna vía TienditApp", string? replyTo = null)
        => new(to, "Factura F-1 de Café Luna", "<p>Hola</p>", senderName, replyTo,
            [new EmailAttachment("F-1_UUID.xml", "application/xml", Xml), new EmailAttachment("F-1_UUID.pdf", "application/pdf", Pdf)]);

    /// <summary>The top headers of the email, unfolded.</summary>
    private static Dictionary<string, string> Headers(string message)
    {
        var top = message[..message.IndexOf("\r\n\r\n", StringComparison.Ordinal)];
        var unfolded = Regex.Replace(top, "\r\n[ \t]+", " ");
        return unfolded.Split("\r\n")
            .Select(line => line.Split(':', 2))
            .Where(parts => parts.Length == 2)
            .GroupBy(parts => parts[0], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First()[1].Trim(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>A header with its encoded words (RFC 2047) decoded; the space between two of them does not count.</summary>
    private static string Decoded(string header)
    {
        var joined = AdjacentEncodedWords().Replace(header, "?==?");
        return EncodedWord().Replace(joined, match => match.Groups["encoding"].Value.Equals("B", StringComparison.OrdinalIgnoreCase)
                ? Encoding.UTF8.GetString(Convert.FromBase64String(match.Groups["text"].Value))
                : QuotedPrintable(match.Groups["text"].Value))
            .Replace("\"", string.Empty);
    }

    private static string QuotedPrintable(string text)
    {
        var bytes = new List<byte>();
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '=' && i + 2 < text.Length)
            {
                bytes.Add(Convert.ToByte(text.Substring(i + 1, 2), 16));
                i += 2;
            }
            else
            {
                bytes.Add((byte)(text[i] == '_' ? ' ' : text[i]));
            }
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    [GeneratedRegex(@"\?=\s+=\?")]
    private static partial Regex AdjacentEncodedWords();

    [GeneratedRegex(@"=\?utf-8\?(?<encoding>[BQ])\?(?<text>[^?]*)\?=", RegexOptions.IgnoreCase)]
    private static partial Regex EncodedWord();

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
