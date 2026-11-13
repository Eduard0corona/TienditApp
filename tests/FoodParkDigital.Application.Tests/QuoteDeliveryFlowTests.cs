using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Exceptions;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Messaging;
using FoodParkDigital.Application.Messaging.Templates;
using FoodParkDigital.Application.Quotes;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Messaging.Entities;
using FoodParkDigital.Domain.Messaging.Enums;
using FoodParkDigital.Domain.Quotes;
using FoodParkDigital.Domain.Quotes.Entities;
using FoodParkDigital.Domain.Quotes.Events;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The quote reaches the customer from TienditApp (Fase 29D, ADR 0053 point 16): «Mandar por correo» queues an email that the
/// outbox sends in the name of the business, with the quote in its body; «Mandar por WhatsApp» queues a message from the
/// business's number, with its template or TienditApp's text. Only a valid sent version goes, within its limits, and each
/// send stays in that version's history.
/// </summary>
public class QuoteDeliveryFlowTests
{
    private const string AppUrl = "https://app.tienditapp.mx";

    [Fact]
    public async Task AnEmail_ShouldGoOutFromTheOutbox_InTheNameOfTheBusiness_WithTheQuoteAndItsLink()
    {
        await using var shop = await Shop.OpenAsync();
        var quote = await shop.SentAsync();

        var asked = await shop.EmailAsync(quote.Id, " valeria@correo.mx ");

        Assert.True(asked.IsSuccess, asked.Error);
        var email = await shop.Context.QuoteEmails.SingleAsync();
        Assert.Equal((quote.Id, "valeria@correo.mx", true), (email.QuoteId, email.To, email.IsPending));

        await shop.DeliverAsync(email.Id);

        var sent = Assert.Single(shop.Email.Documents);
        Assert.Equal(("valeria@correo.mx", "Cotización COT-000001 de Negocio plomero", "Negocio plomero vía TienditApp", "plomero@demo.com"),
            (sent.To, sent.Subject, sent.SenderName, sent.ReplyTo));
        // Its PDF goes attached (Fase 31M), and the body says so.
        var pdf = Assert.Single(sent.Attachments);
        Assert.Equal(("cotizacion-COT-000001.pdf", "application/pdf"), (pdf.FileName, pdf.ContentType));
        Assert.Equal(RecordingQuotePdfRenderer.Pdf(Assert.Single(shop.Pdfs.Rendered)), pdf.Content);
        Assert.Contains("La cotización va adjunta en PDF.", sent.HtmlBody);
        Assert.Contains("Cambio de calentador", sent.HtmlBody);
        Assert.Contains("vale hasta el 19 de octubre de 2026", sent.HtmlBody);
        Assert.Contains("Total: $1,160.00", sent.HtmlBody);
        Assert.Contains($"href=\"{AppUrl}/cotizacion#{quote.TrackingToken}\"", sent.HtmlBody);
        Assert.Contains("responde a este correo", sent.HtmlBody);
        Assert.Equal(SalesTestFixture.Now, (await shop.Context.QuoteEmails.SingleAsync()).SentAt);
        // The detail shows it.
        var detail = await shop.GetAsync(quote.Id);
        Assert.Equal((email.Id, (DateTimeOffset?)SalesTestFixture.Now), (Assert.Single(detail.Emails).Id, detail.Emails[0].SentAt));
    }

    [Fact]
    public async Task TheEmail_ShouldEncodeWhatThePeopleWrote()
    {
        await using var shop = await Shop.OpenAsync();
        var quote = await shop.SentAsync(customerName: "<b>Valeria</b>", line: "Tubo <script>alert(1)</script>", note: "Pago & entrega\nel lunes");

        await shop.EmailAsync(quote.Id, "valeria@correo.mx");
        await shop.DeliverAsync((await shop.Context.QuoteEmails.SingleAsync()).Id);

        var body = Assert.Single(shop.Email.Documents).HtmlBody;
        Assert.DoesNotContain("<script>", body);
        Assert.DoesNotContain("<b>Valeria</b>", body);
        Assert.Contains("Tubo &lt;script&gt;alert(1)&lt;/script&gt;", body);
        Assert.Contains("&lt;b&gt;Valeria&lt;/b&gt;", body);
        Assert.Contains("Pago &amp; entrega<br>el lunes", body);
    }

    [Fact]
    public async Task TheEmail_ShouldEnd_RefusedOrNotSetUp_TryAgainWhenTheServerDoesNotAnswer_AndEndAfterTheLastTry()
    {
        await using var shop = await Shop.OpenAsync();
        var quote = await shop.SentAsync();
        var ids = new List<Guid>();
        for (var i = 0; i < 4; i++)
        {
            await shop.EmailAsync(quote.Id, $"cliente{i}@correo.mx");
            ids.Add((await shop.Context.QuoteEmails.OrderByDescending(e => e.RequestedAt).ThenBy(e => e.To).ToListAsync()).Single(e => e.To == $"cliente{i}@correo.mx").Id);
        }

        shop.Email.DocumentsOutcome = EmailSendOutcome.Rejected;
        await shop.DeliverAsync(ids[0]);
        shop.Email.DocumentsOutcome = EmailSendOutcome.NotConfigured;
        await shop.DeliverAsync(ids[1]);
        shop.Email.DocumentsOutcome = EmailSendOutcome.Sent;
        shop.Email.DocumentsFailure = new InvalidOperationException("the server did not answer");
        await Assert.ThrowsAsync<InvalidOperationException>(() => shop.DeliverAsync(ids[2]));
        await shop.DeliverAsync(ids[3], attempt: 8, maxAttempts: 8);

        var emails = await shop.Context.QuoteEmails.AsNoTracking().ToDictionaryAsync(e => e.Id);
        Assert.Equal(QuoteEmailFailure.Rejected, emails[ids[0]].Failure);
        Assert.Equal(QuoteEmailFailure.NotConfigured, emails[ids[1]].Failure);
        // Not answering keeps it pending, with the failed try kept although the handler threw.
        Assert.Equal((true, 1), (emails[ids[2]].IsPending, emails[ids[2]].FailedAttempts));
        Assert.Equal(QuoteEmailFailure.NotDelivered, emails[ids[3]].Failure);
    }

    [Fact]
    public async Task TheOutbox_ShouldSendItOnce_EvenIfItRunsTwice()
    {
        await using var shop = await Shop.OpenAsync();
        var quote = await shop.SentAsync();
        await shop.EmailAsync(quote.Id, "valeria@correo.mx");
        var id = (await shop.Context.QuoteEmails.SingleAsync()).Id;

        await shop.DeliverAsync(id);
        await shop.DeliverAsync(id);

        Assert.Single(shop.Email.Documents);
    }

    [Fact]
    public async Task OnlyAValidSentVersion_ShouldGoToTheCustomer_ByEitherChannel()
    {
        await using var shop = await Shop.OpenAsync();
        var draft = await shop.DraftAsync();
        var expired = await shop.SentAsync();

        Assert.Equal(QuoteRules.NotSendableMessage, (await shop.EmailAsync(draft.Id, "valeria@correo.mx")).Error);
        Assert.Equal(QuoteRules.NotSendableMessage, (await shop.WhatsAppAsync(draft.Id, "5522107765")).Error);
        shop.Clock.UtcNow = SalesTestFixture.Now.AddDays(16);
        Assert.Equal(QuoteRules.NotSendableMessage, (await shop.EmailAsync(expired.Id, "valeria@correo.mx")).Error);
        Assert.Equal(QuoteRules.NotSendableMessage, (await shop.WhatsAppAsync(expired.Id, "5522107765")).Error);
        Assert.Equal(0, await shop.Context.QuoteEmails.CountAsync() + await shop.Context.OutgoingMessages.CountAsync());
    }

    [Fact]
    public async Task AnotherBusinesssQuote_ShouldNotBeThere()
    {
        await using var shop = await Shop.OpenAsync();
        var theirs = await shop.SentAsync();
        await shop.OtherBusinessAsync();

        await Assert.ThrowsAsync<EntityNotFoundException>(() => shop.EmailAsync(theirs.Id, "valeria@correo.mx"));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => shop.WhatsAppAsync(theirs.Id, "5522107765"));
        Assert.Equal(0, await shop.Context.QuoteEmails.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task AVersion_ShouldGoFiveTimesADayByEachChannel_AndABusinessThreeHundred()
    {
        await using var shop = await Shop.OpenAsync();
        var quote = await shop.SentAsync();
        for (var i = 0; i < QuoteRules.MaxSendsPerQuoteVersion; i++)
        {
            Assert.True((await shop.EmailAsync(quote.Id, "valeria@correo.mx")).IsSuccess);
            Assert.True((await shop.WhatsAppAsync(quote.Id, "5522107765")).IsSuccess);
        }

        Assert.Equal(QuoteRules.TooManySendsMessage, (await shop.EmailAsync(quote.Id, "valeria@correo.mx")).Error);
        Assert.Equal(QuoteRules.TooManySendsMessage, (await shop.WhatsAppAsync(quote.Id, "5522107765")).Error);
        // A day later it goes again.
        shop.Clock.UtcNow = SalesTestFixture.Now.AddDays(1).AddMinutes(1);
        Assert.True((await shop.EmailAsync(quote.Id, "valeria@correo.mx")).IsSuccess);

        // Another version of the business brings the day to 299 emails: the 300th goes, the 301st does not.
        var other = await shop.SentAsync();
        var otherQuote = await shop.Context.Quotes.AsNoTracking().SingleAsync(q => q.Id == other.Id);
        var today = DateOnly.FromDateTime(shop.Clock.UtcNow.UtcDateTime);
        var since = shop.Clock.UtcNow - QuoteDeliveries.Day;
        var already = await shop.Context.QuoteEmails.CountAsync(e => e.RequestedAt > since);
        for (var i = already; i < QuoteRules.MaxSendsPerBusiness - 1; i++)
            shop.Context.QuoteEmails.Add(QuoteEmail.Request(otherQuote, "otro@correo.mx", Guid.NewGuid(), today, shop.Clock.UtcNow));
        await shop.Context.SaveChangesAsync();
        Assert.True((await shop.EmailAsync(quote.Id, "valeria@correo.mx")).IsSuccess);
        Assert.Equal(QuoteRules.TooManyBusinessSendsMessage, (await shop.EmailAsync(quote.Id, "valeria@correo.mx")).Error);
    }

    [Fact]
    public async Task ASend_ShouldNotGoOut_OnceTheVersionStoppedBeingValid_BeforeItLeft()
    {
        await using var shop = await Shop.OpenAsync();
        var accepted = await shop.SentAsync();
        var expired = await shop.SentAsync();
        await shop.EmailAsync(accepted.Id, "valeria@correo.mx");
        await shop.EmailAsync(expired.Id, "valeria@correo.mx");
        await shop.WhatsAppAsync(accepted.Id, "5522107765");
        var emails = await shop.Context.QuoteEmails.AsNoTracking().ToDictionaryAsync(e => e.QuoteId, e => e.Id);
        var message = await shop.Context.OutgoingMessages.AsNoTracking().SingleAsync();

        // The customer accepted one meanwhile; the mail server answered the other only after its validity.
        Assert.True((await shop.Handler.Handle(new RecordQuoteReplyCommand(accepted.Id, true, QuoteReplyChannel.Phone, null), CancellationToken.None)).IsSuccess);
        await shop.DeliverAsync(emails[accepted.Id]);
        shop.Clock.UtcNow = SalesTestFixture.Now.AddDays(16);
        await shop.DeliverAsync(emails[expired.Id]);
        var outcome = await shop.Sender().SendAsync(message, CancellationToken.None);

        Assert.Empty(shop.Email.Documents);
        Assert.All(await shop.Context.QuoteEmails.AsNoTracking().ToListAsync(), e => Assert.Equal(QuoteEmailFailure.NoLongerValid, e.Failure));
        Assert.Equal((OutgoingSendDecision.Skipped, QuoteDeliveries.NoLongerValidReason), (outcome.Decision, outcome.Reason));
    }

    [Fact]
    public async Task ANumberThatOptedOut_ShouldGetTheQuote_OnlyWhenItIsTheCustomersOwn()
    {
        await using var shop = await Shop.OpenAsync();
        var quote = await shop.SentAsync();
        shop.Context.MessagingOptOuts.AddRange(
            new MessagingOptOut(shop.Tenant.Id, "+525522107765", OptOutSource.CustomerKeyword, SalesTestFixture.Now),
            new MessagingOptOut(shop.Tenant.Id, "+525511112222", OptOutSource.Staff, SalesTestFixture.Now));
        await shop.Context.SaveChangesAsync();

        Assert.Equal(QuoteDeliveries.OptedOutMessage, (await shop.WhatsAppAsync(quote.Id, "55 1111 2222")).Error);
        // The customer of the quote asked for it, in whichever form the number is written.
        Assert.True((await shop.WhatsAppAsync(quote.Id, "+52 1 55 2210 7765")).IsSuccess);
    }

    [Fact]
    public async Task AWhatsApp_ShouldCountFiveByVersion_AndThreeHundredByBusiness()
    {
        await using var shop = await Shop.OpenAsync();
        var first = await shop.SentAsync();
        var second = await shop.SentAsync();
        for (var i = 0; i < QuoteRules.MaxSendsPerQuoteVersion; i++)
            Assert.True((await shop.WhatsAppAsync(first.Id, "5522107765")).IsSuccess);

        Assert.Equal(QuoteRules.TooManySendsMessage, (await shop.WhatsAppAsync(first.Id, "5522107765")).Error);
        // Another version of the business still goes, until the business reaches its limit: the 300th goes, the 301st does not.
        Assert.True((await shop.WhatsAppAsync(second.Id, "5522107765")).IsSuccess);
        var other = await shop.SentAsync();
        var otherQuote = await shop.Context.Quotes.AsNoTracking().SingleAsync(q => q.Id == other.Id);
        var today = DateOnly.FromDateTime(shop.Clock.UtcNow.UtcDateTime);
        var already = await shop.Context.OutgoingMessages.CountAsync(m => m.QuoteId != null);
        for (var i = already; i < QuoteRules.MaxSendsPerBusiness - 1; i++)
            shop.Context.OutgoingMessages.Add(OutgoingMessage.ForQuote(otherQuote, "+525533334444", "Otro", "Hola", null, "es_MX", [],
                Guid.NewGuid(), today, shop.Clock.UtcNow));
        await shop.Context.SaveChangesAsync();
        Assert.True((await shop.WhatsAppAsync(second.Id, "5522107765")).IsSuccess);
        Assert.Equal(QuoteRules.TooManyBusinessSendsMessage, (await shop.WhatsAppAsync(second.Id, "5522107765")).Error);
    }

    [Theory]
    [InlineData("", QuoteRules.EmailMessage)]
    [InlineData("valeria", InvoiceEmailAddress.InvalidMessage)]
    [InlineData("Valeria <valeria@correo.mx>", InvoiceEmailAddress.InvalidMessage)]
    public async Task TheEmailCommand_ShouldNeedOnePlainAddress(string email, string message)
    {
        var errors = await new SendQuoteByEmailCommandValidator().ValidateAsync(new SendQuoteByEmailCommand(Guid.NewGuid(), email));
        Assert.Equal(message, Assert.Single(errors.Errors).ErrorMessage);
    }

    [Fact]
    public async Task AWhatsApp_ShouldNeedTheBusinessesWhatsAppConnected_AndANumberThatIsOne()
    {
        await using var shop = await Shop.OpenAsync(connected: false);
        var quote = await shop.SentAsync();

        Assert.Equal(QuoteDeliveries.NotConnectedMessage, (await shop.WhatsAppAsync(quote.Id, "5522107765")).Error);
        Assert.False((await shop.GetAsync(quote.Id)).WhatsAppConnected);
        await shop.ConnectWhatsAppAsync();
        Assert.Equal(InvoiceWhatsApp.InvalidMessage, (await shop.WhatsAppAsync(quote.Id, "123")).Error);
        Assert.True((await shop.GetAsync(quote.Id)).WhatsAppConnected);
    }

    [Fact]
    public async Task AWhatsApp_ShouldGoWithTienditAppsText_ToTheNumberNormalized_WithWhatItCostsAndUntilWhen()
    {
        await using var shop = await Shop.OpenAsync();
        var quote = await shop.SentAsync();

        var queued = await shop.WhatsAppAsync(quote.Id, "55 2210 7765");

        Assert.True(queued.IsSuccess, queued.Error);
        var message = await shop.Context.OutgoingMessages.SingleAsync();
        Assert.Equal((MessagePurpose.QuoteDocument, quote.Id, "+525522107765", (string?)null),
            (message.Purpose, message.QuoteId, message.Phone, message.ProviderTemplateName));
        Assert.Equal(
            $"Hola Valeria Ruiz: Negocio plomero te manda la cotización COT-000001 por $1,160.00, vale hasta el 19 de octubre de 2026. Mírala aquí: {AppUrl}/cotizacion#{quote.TrackingToken}",
            message.Body);
        var detail = await shop.GetAsync(quote.Id);
        Assert.Equal((message.Id, OutgoingMessageStatus.Pending), (Assert.Single(detail.WhatsApps).Id, detail.WhatsApps[0].Status));
    }

    [Fact]
    public async Task AWhatsApp_ShouldGoWithTheBusinessesTemplate_AsMetaApprovedIt_WhileItIsActive()
    {
        await using var shop = await Shop.OpenAsync();
        var quote = await shop.SentAsync();
        var template = new MessageTemplate(shop.Tenant.Id, "Cotización", MessagePurpose.QuoteDocument,
            "{negocio}: tu cotización {folio} de {monto} vale hasta el {fecha}. {enlace}", "cotizacion_lista");
        shop.Context.MessageTemplates.Add(template);
        await shop.Context.SaveChangesAsync();
        Assert.True((await shop.Handler.Handle(new SetQuoteWhatsAppTemplateCommand(template.Id), CancellationToken.None)).IsSuccess);

        await shop.WhatsAppAsync(quote.Id, "5522107765");
        template.SetActive(false);
        await shop.Context.SaveChangesAsync();
        await shop.WhatsAppAsync(quote.Id, "5522107765");

        var messages = await shop.Context.OutgoingMessages.OrderBy(m => m.ProviderTemplateName == null).ToListAsync();
        Assert.Equal("cotizacion_lista", messages[0].ProviderTemplateName);
        Assert.Equal(["Negocio plomero", "COT-000001", "$1,160.00", "19 de octubre de 2026", $"{AppUrl}/cotizacion#{quote.TrackingToken}"],
            messages[0].GetParameters());
        // Turned off, TienditApp's text goes.
        Assert.Null(messages[1].ProviderTemplateName);
        Assert.StartsWith("Hola Valeria Ruiz:", messages[1].Body);
    }

    [Fact]
    public async Task TheBusiness_ShouldPickOnlyAnActiveTemplateForQuotes_OfItsOwn()
    {
        await using var shop = await Shop.OpenAsync();
        var forQuotes = new MessageTemplate(shop.Tenant.Id, "Cotización", MessagePurpose.QuoteDocument, "Tu cotización {folio}: {enlace}");
        var forCampaigns = new MessageTemplate(shop.Tenant.Id, "Promo", MessagePurpose.Campaign, "Hola {nombre}");
        shop.Context.MessageTemplates.AddRange(forQuotes, forCampaigns);
        await shop.Context.SaveChangesAsync();
        var theirs = await shop.OtherBusinessTemplateAsync();

        Assert.Equal(QuotesHandler.TemplateNotFoundMessage,
            (await shop.Handler.Handle(new SetQuoteWhatsAppTemplateCommand(forCampaigns.Id), CancellationToken.None)).Error);
        Assert.Equal(QuotesHandler.TemplateNotFoundMessage,
            (await shop.Handler.Handle(new SetQuoteWhatsAppTemplateCommand(theirs), CancellationToken.None)).Error);
        var picked = await shop.Handler.Handle(new SetQuoteWhatsAppTemplateCommand(forQuotes.Id), CancellationToken.None);

        Assert.True(picked.IsSuccess, picked.Error);
        Assert.Equal((forQuotes.Id, true), (picked.Value!.WhatsAppTemplateId, picked.Value.WhatsAppConnected));
        Assert.Equal([forQuotes.Id], picked.Value.WhatsAppTemplates.Select(t => t.Id));
        Assert.Equal(1, await shop.Context.AuditLogs.CountAsync(a => a.Action == "QuoteWhatsAppTemplateChanged"));
        // Turned off since, it reads as TienditApp's text.
        forQuotes.SetActive(false);
        await shop.Context.SaveChangesAsync();
        Assert.Null((await shop.Handler.Handle(new GetQuoteSettingsQuery(), CancellationToken.None)).WhatsAppTemplateId);
        Assert.True((await shop.Handler.Handle(new SetQuoteWhatsAppTemplateCommand(null), CancellationToken.None)).IsSuccess);
    }

    [Fact]
    public async Task TheHistory_ShouldShowTheSendsOfThatVersion_TheNewestFirst_FiveOfEach()
    {
        await using var shop = await Shop.OpenAsync();
        var first = await shop.SentAsync();
        var second = await shop.SentAsync();
        // Six of each channel over the days (past the limit of a day), seeded as they were asked for.
        var version = await shop.Context.Quotes.AsNoTracking().SingleAsync(q => q.Id == first.Id);
        for (var i = 0; i < 6; i++)
        {
            var at = SalesTestFixture.Now.AddMinutes(i);
            shop.Context.QuoteEmails.Add(QuoteEmail.Request(version, $"c{i}@correo.mx", Guid.NewGuid(), new DateOnly(2026, 10, 4), at));
            shop.Context.OutgoingMessages.Add(OutgoingMessage.ForQuote(version, $"+52552210776{i}", "Valeria", "Hola", null, "es_MX", [],
                Guid.NewGuid(), new DateOnly(2026, 10, 4), at));
        }
        await shop.Context.SaveChangesAsync();
        await shop.EmailAsync(second.Id, "otra@correo.mx");

        var detail = await shop.GetAsync(first.Id);

        Assert.Equal(["c5@correo.mx", "c4@correo.mx", "c3@correo.mx", "c2@correo.mx", "c1@correo.mx"], detail.Emails.Select(e => e.To));
        Assert.Equal(["+525522107765", "+525522107764", "+525522107763", "+525522107762", "+525522107761"], detail.WhatsApps.Select(m => m.Phone));
        Assert.Equal(["otra@correo.mx"], (await shop.GetAsync(second.Id)).Emails.Select(e => e.To));
        Assert.Empty((await shop.GetAsync(second.Id)).WhatsApps);
    }

    [Fact]
    public async Task AQuotesWhatsApp_ShouldReachACustomerWhoOptedOut_ButStayOutOfTheCampaignsMessages()
    {
        await using var shop = await Shop.OpenAsync();
        var quote = await shop.SentAsync();
        await shop.WhatsAppAsync(quote.Id, "5522107765");
        shop.Context.MessagingOptOuts.Add(new MessagingOptOut(shop.Tenant.Id, "+525522107765", OptOutSource.CustomerKeyword, SalesTestFixture.Now));
        await shop.Context.SaveChangesAsync();
        var whatsApp = new CreditTestKit.FakeWhatsApp();
        var sender = new OutgoingMessageSender(shop.Context, whatsApp,
            new MessagingConsentService(shop.Context, shop.Fixture.TenantService, shop.Clock), shop.Clock);

        var outcome = await sender.SendAsync(await shop.Context.OutgoingMessages.SingleAsync(), CancellationToken.None);

        Assert.Equal(OutgoingSendDecision.Sent, outcome.Decision);
        Assert.Equal([("text", "+525522107765")], whatsApp.Sent);
        var listed = await new Messaging.Campaigns.GetOutgoingMessagesQueryHandler(shop.Context)
            .Handle(new Messaging.Campaigns.GetOutgoingMessagesQuery(), CancellationToken.None);
        Assert.Empty(listed.Items);
    }

    [Fact]
    public async Task OnlyAnOwnerOrAdmin_ShouldWriteATemplateForQuotes()
    {
        await using var shop = await Shop.OpenAsync();
        var access = new Application.Common.Security.CurrentAccessContext(shop.Fixture.CurrentUser, shop.Fixture.TenantService, new Access(isManager: false));

        var error = await Assert.ThrowsAsync<ForbiddenAccessException>(
            () => MessageTemplateLimits.EnsureCanWriteAsync(MessagePurpose.QuoteDocument, access, CancellationToken.None));
        Assert.Equal(MessageTemplateLimits.QuoteManagersOnlyMessage, error.Message);
    }

    [Fact]
    public async Task TheEmail_ShouldGoWithoutItsPdf_WhenThePdfCannotBeRendered_NeverLate()
    {
        await using var shop = await Shop.OpenAsync();
        var quote = await shop.SentAsync();
        await shop.EmailAsync(quote.Id, "valeria@correo.mx");
        shop.Pdfs.Failure = new InvalidOperationException("the PDF broke");

        await shop.DeliverAsync((await shop.Context.QuoteEmails.SingleAsync()).Id);

        var sent = Assert.Single(shop.Email.Documents);
        Assert.Empty(sent.Attachments);
        Assert.DoesNotContain("adjunta en PDF", sent.HtmlBody);
        Assert.Contains($"href=\"{AppUrl}/cotizacion#{quote.TrackingToken}\"", sent.HtmlBody);
        Assert.Equal(SalesTestFixture.Now, (await shop.Context.QuoteEmails.SingleAsync()).SentAt);
    }

    [Fact]
    public async Task TheBusiness_ShouldDownloadThePdfOfAnyVersion_ADraftToo_AsItReadsOnItsDay()
    {
        await using var shop = await Shop.OpenAsync();
        var sent = await shop.SentAsync(note: "Incluye material.");
        var draft = await shop.DraftAsync(customerName: "Luis Gómez", line: "Revisión de fuga");

        var file = await shop.PdfAsync(sent.Id);

        Assert.True(file.IsSuccess, file.Error);
        Assert.Equal(("application/pdf", "cotizacion-COT-000001.pdf"), (file.Value!.ContentType, file.Value.FileName));
        var model = Assert.Single(shop.Pdfs.Rendered);
        Assert.Equal(RecordingQuotePdfRenderer.Pdf(model), Read(file.Value.Content));
        Assert.Equal((shop.Tenant.Id, "Negocio plomero", (string?)"plomero@demo.com", "COT-000001", 1, QuoteStatus.Sent, "Valeria Ruiz"),
            (model.TenantId, model.BusinessName, model.BusinessEmail, model.Number, model.Revision, model.Status, model.CustomerName));
        Assert.Equal(((DateOnly?)new DateOnly(2026, 10, 4), (DateOnly?)new DateOnly(2026, 10, 19), (DateOnly?)null),
            (model.SentOn, model.ValidUntil, model.RepliedOn));
        Assert.Equal([new QuotePdfLine("Cambio de calentador", 1m, 1160m, 1160m)], model.Lines);
        Assert.Equal((1000m, 160m, 1160m, 0m, 1160m, true, (string?)"Incluye material."),
            (model.SubTotal, model.Tax, model.Total, model.VisitCredit, model.ToPay, model.PricesIncludeTax, model.Note));

        Assert.True((await shop.PdfAsync(draft.Id)).IsSuccess);
        var ofDraft = shop.Pdfs.Rendered[1];
        Assert.Equal((QuoteStatus.Draft, "Luis Gómez", (DateOnly?)null, (DateOnly?)null, 15),
            (ofDraft.Status, ofDraft.CustomerName, ofDraft.SentOn, ofDraft.ValidUntil, ofDraft.ValidityDays));

        // Past its validity on the business's day it reads expired, as its page says.
        shop.Clock.UtcNow = SalesTestFixture.Now.AddDays(16);
        Assert.True((await shop.PdfAsync(sent.Id)).IsSuccess);
        Assert.Equal(QuoteStatus.Expired, shop.Pdfs.Rendered[2].Status);
    }

    [Fact]
    public async Task ACorrectedVersion_ShouldNameItsPdfWithItsVersion_AndTheReplacedOneSayItWasReplaced()
    {
        await using var shop = await Shop.OpenAsync();
        var first = await shop.SentAsync();
        var revised = await shop.Handler.Handle(new ReviseQuoteCommand(first.Id), CancellationToken.None);
        Assert.True(revised.IsSuccess, revised.Error);
        Assert.True((await shop.Handler.Handle(new SendQuoteCommand(revised.Value!.Id), CancellationToken.None)).IsSuccess);

        var second = await shop.PdfAsync(revised.Value.Id);
        var replaced = await shop.PdfAsync(first.Id);

        Assert.Equal("cotizacion-COT-000001-v2.pdf", second.Value!.FileName);
        Assert.Equal("cotizacion-COT-000001.pdf", replaced.Value!.FileName);
        Assert.Equal([QuoteStatus.Sent, QuoteStatus.Superseded], shop.Pdfs.Rendered.Select(model => model.Status));
    }

    [Fact]
    public async Task AnotherBusinesssQuote_ShouldHaveNoPdf()
    {
        await using var shop = await Shop.OpenAsync();
        var quote = await shop.SentAsync();
        await shop.OtherBusinessAsync();

        var file = await shop.PdfAsync(quote.Id);

        Assert.True(file.IsFailure);
        Assert.Equal(QuotePdfs.NotFoundMessage, file.Error);
        Assert.Empty(shop.Pdfs.Rendered);
    }

    [Fact]
    public async Task TheCustomer_ShouldDownloadThePdfOfTheirLink_OnlyWhileItOpens()
    {
        await using var shop = await Shop.OpenAsync();
        var quote = await shop.SentAsync();
        var token = quote.TrackingToken!;

        var file = await shop.PublicPdfAsync(token);

        Assert.NotNull(file);
        Assert.Equal(("application/pdf", "cotizacion-COT-000001.pdf"), (file.ContentType, file.FileName));
        var model = Assert.Single(shop.Pdfs.Rendered);
        Assert.Equal(RecordingQuotePdfRenderer.Pdf(model), Read(file.Content));
        Assert.Equal((shop.Tenant.Id, QuoteStatus.Sent, "Valeria Ruiz", (string?)"plomero@demo.com"),
            (model.TenantId, model.Status, model.CustomerName, model.BusinessEmail));

        // A link unknown, or changed, has none.
        Assert.Null(await shop.PublicPdfAsync(new string('a', token.Length)));
        Assert.Null(await shop.PublicPdfAsync(token[..^1] + (token[^1] == 'a' ? 'b' : 'a')));
        // Up to 30 days after its validity (the 19th of October), expired; the day after, none.
        shop.Clock.UtcNow = new DateTimeOffset(2026, 11, 18, 18, 0, 0, TimeSpan.Zero);
        Assert.NotNull(await shop.PublicPdfAsync(token));
        Assert.Equal(QuoteStatus.Expired, shop.Pdfs.Rendered[^1].Status);
        shop.Clock.UtcNow = new DateTimeOffset(2026, 11, 19, 18, 0, 0, TimeSpan.Zero);
        Assert.Null(await shop.PublicPdfAsync(token));
        Assert.Equal(2, shop.Pdfs.Rendered.Count);
    }

    [Fact]
    public async Task AWhatsAppWithoutATemplate_ShouldCarryThePdfAsItsDocument_WithItsTextAsTheCaption()
    {
        await using var shop = await Shop.OpenAsync();
        var quote = await shop.SentAsync();
        await shop.WhatsAppAsync(quote.Id, "5522107765");
        var whatsApp = new CreditTestKit.FakeWhatsApp();
        var documents = new RecordingWhatsAppDocuments();
        var message = await shop.Context.OutgoingMessages.SingleAsync();

        var outcome = await shop.Sender(whatsApp, documents).SendAsync(message, CancellationToken.None);

        Assert.Equal((OutgoingSendDecision.Sent, "wamid.doc.1"), (outcome.Decision, outcome.ProviderMessageId));
        Assert.Empty(whatsApp.Sent);
        var sent = Assert.Single(documents.Sent);
        Assert.Equal(("+525522107765", "cotizacion-COT-000001.pdf", "application/pdf", message.Body, (string?)null),
            (sent.Phone, sent.Document.FileName, sent.Document.ContentType, sent.Caption, sent.TemplateName));
        Assert.Equal(RecordingQuotePdfRenderer.Pdf(Assert.Single(shop.Pdfs.Rendered)), sent.Document.Content);
    }

    [Fact]
    public async Task AWhatsAppWithATemplate_OrWhosePdfCannotBeRendered_ShouldGoAsBefore()
    {
        await using var shop = await Shop.OpenAsync();
        var quote = await shop.SentAsync();
        var template = new MessageTemplate(shop.Tenant.Id, "Cotización", MessagePurpose.QuoteDocument,
            "{negocio}: tu cotización {folio} de {monto} vale hasta el {fecha}. {enlace}", "cotizacion_lista");
        shop.Context.MessageTemplates.Add(template);
        await shop.Context.SaveChangesAsync();
        Assert.True((await shop.Handler.Handle(new SetQuoteWhatsAppTemplateCommand(template.Id), CancellationToken.None)).IsSuccess);
        await shop.WhatsAppAsync(quote.Id, "5522107765");
        var whatsApp = new CreditTestKit.FakeWhatsApp();
        var documents = new RecordingWhatsAppDocuments();
        var sender = shop.Sender(whatsApp, documents);

        // The business's template may have no document header: it goes as Meta approved it, without the PDF.
        var withTemplate = await sender.SendAsync(await shop.Context.OutgoingMessages.SingleAsync(), CancellationToken.None);
        Assert.Equal(OutgoingSendDecision.Sent, withTemplate.Decision);
        Assert.Equal([("template:cotizacion_lista", "+525522107765")], whatsApp.Sent);
        Assert.Empty(shop.Pdfs.Rendered);

        // Without a template but without its PDF, the text goes.
        template.SetActive(false);
        await shop.Context.SaveChangesAsync();
        await shop.WhatsAppAsync(quote.Id, "5522107765");
        shop.Pdfs.Failure = new InvalidOperationException("the PDF broke");
        var text = await shop.Context.OutgoingMessages.SingleAsync(m => m.ProviderTemplateName == null);
        var withoutPdf = await sender.SendAsync(text, CancellationToken.None);
        Assert.Equal(OutgoingSendDecision.Sent, withoutPdf.Decision);
        Assert.Equal(("text", "+525522107765"), whatsApp.Sent[^1]);
        Assert.Empty(documents.Sent);
    }

    private static byte[] Read(Stream content)
    {
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        return buffer.ToArray();
    }

    private sealed class Links : IAppLinks
    {
        public string? BaseUrl => AppUrl;
    }

    private sealed class Access(bool isManager) : ITenantAccessService
    {
        private const TenantCapability Modules = TenantCapability.Service | TenantCapability.Appointments;

        public Task<TenantAccessSnapshot> GetAccessSnapshotAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
            => Task.FromResult(new TenantAccessSnapshot(true, Modules, [], IsManager: isManager));

        public Task<bool> HasActiveAccessAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> HasCapabilityAsync(Guid tenantId, TenantCapability capability, CancellationToken cancellationToken)
            => Task.FromResult((capability & Modules) == capability);

        public Task<bool> HasAnyCapabilityAsync(Guid tenantId, TenantCapability capabilities, CancellationToken cancellationToken)
            => Task.FromResult((capabilities & Modules) != 0);

        public Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyCollection<string>>([]);

        public Task<bool> HasPermissionAsync(Guid userId, Guid tenantId, TenantPermission permission, CancellationToken cancellationToken)
            => Task.FromResult(true);
    }

    private sealed class Shop : IAsyncDisposable
    {
        private Shop(SalesTestFixture fixture, Tenant tenant)
        {
            Fixture = fixture;
            Tenant = tenant;
        }

        public SalesTestFixture Fixture { get; }
        public Tenant Tenant { get; }
        public RecordingEmailService Email { get; } = new();
        public RecordingQuotePdfRenderer Pdfs { get; } = new();
        public SalesDbContext Context => Fixture.Context;
        public SalesClock Clock => Fixture.Clock;
        public QuotesHandler Handler => new(Context, Fixture.TenantService, Fixture.CurrentUser, Clock);

        private QuoteDeliveryHandler Deliveries => new(
            Context, Fixture.TenantService, Fixture.CurrentUser, new PhoneNormalizationService(), new QuoteSendingLock(Context), new Links(), Clock);

        public static async Task<Shop> OpenAsync(bool connected = true)
        {
            var fixture = SalesTestFixture.Create();
            var tenant = await fixture.AddTenantAsync(BusinessType.Service, TenantCapability.Service | TenantCapability.Appointments,
                pricesIncludeTax: true, slug: "plomero");
            var shop = new Shop(fixture, tenant);
            if (connected)
                await shop.ConnectWhatsAppAsync();
            return shop;
        }

        public async Task ConnectWhatsAppAsync()
        {
            var config = new WhatsAppConfig(Tenant.Id);
            config.Update(isEnabled: true, accessToken: "token", phoneNumberId: "2233445566", wabaId: "1122334455", displayPhoneNumber: null);
            Context.WhatsAppConfigs.Add(config);
            await Context.SaveChangesAsync();
        }

        /// <summary>A draft for Valeria with one line ($1,160 with IVA inside).</summary>
        public async Task<QuoteDto> DraftAsync(string customerName = "Valeria Ruiz", string line = "Cambio de calentador", string? note = null)
        {
            var created = await new CreateQuoteCommandHandler(Context, Fixture.TenantService, Fixture.CurrentUser, new FolioGenerator(Context), Clock)
                .Handle(new CreateQuoteCommand(null, null, customerName, "5522107765", "valeria@correo.mx"), CancellationToken.None);
            Assert.True(created.IsSuccess, created.Error);
            var quote = created.Value!;
            var updated = await Handler.Handle(new UpdateQuoteDraftCommand(quote.Id, null, customerName, "5522107765", "valeria@correo.mx", 15, note,
                [new QuoteLineRequest { Description = line, Quantity = 1m, UnitPrice = 1160m }]), CancellationToken.None);
            Assert.True(updated.IsSuccess, updated.Error);
            return updated.Value!;
        }

        public async Task<QuoteDto> SentAsync(string customerName = "Valeria Ruiz", string line = "Cambio de calentador", string? note = null)
        {
            var draft = await DraftAsync(customerName, line, note);
            var sent = await Handler.Handle(new SendQuoteCommand(draft.Id), CancellationToken.None);
            Assert.True(sent.IsSuccess, sent.Error);
            return sent.Value!;
        }

        public Task<Result<QuoteEmailDto>> EmailAsync(Guid id, string email)
            => Deliveries.Handle(new SendQuoteByEmailCommand(id, email), CancellationToken.None);

        public Task<Result<QuoteWhatsAppDto>> WhatsAppAsync(Guid id, string phone)
            => Deliveries.Handle(new SendQuoteByWhatsAppCommand(id, phone), CancellationToken.None);

        public Task<QuoteDto> GetAsync(Guid id) => Handler.Handle(new GetQuoteQuery(id), CancellationToken.None);

        public OutgoingMessageSender Sender() => new(Context, new CreditTestKit.FakeWhatsApp(),
            new MessagingConsentService(Context, Fixture.TenantService, Clock), Clock);

        /// <summary>The worker's sender as the application composes it: a quote's message without a template carries its PDF.</summary>
        public OutgoingMessageSender Sender(CreditTestKit.FakeWhatsApp whatsApp, RecordingWhatsAppDocuments documents) => new(
            Context, whatsApp, new MessagingConsentService(Context, Fixture.TenantService, Clock), Clock, cfdi: null,
            new QuoteWhatsAppDocuments(Context, Pdfs, documents, Clock, NullLogger<QuoteWhatsAppDocuments>.Instance));

        /// <summary>The PDF of a version, as the business downloads it.</summary>
        public Task<Result<Orders.Queries.PrivateFileDownload>> PdfAsync(Guid id)
            => new GetQuotePdfQueryHandler(Context, Fixture.TenantService, Pdfs, Clock).Handle(new GetQuotePdfQuery(id), CancellationToken.None);

        /// <summary>The PDF of a link, as its customer downloads it.</summary>
        public Task<Orders.Queries.PrivateFileDownload?> PublicPdfAsync(string token)
            => new GetPublicQuotePdfQueryHandler(Context, Fixture.TenantService, Pdfs, Clock)
                .Handle(new GetPublicQuotePdfQuery(token), CancellationToken.None);

        /// <summary>The outbox runs the email's handler, attempt <paramref name="attempt"/> of <paramref name="maxAttempts"/>.</summary>
        public async Task DeliverAsync(Guid emailId, int attempt = 1, int maxAttempts = 8)
        {
            var handler = new QuoteEmailRequestedHandler(Context, Email, new Links(), Pdfs, Clock, NullLogger<QuoteEmailRequestedHandler>.Instance);
            await handler.HandleAsync(new QuoteEmailRequestedEvent(emailId),
                new OutboxEventContext(Guid.NewGuid(), Tenant.Id, null, null, Clock.UtcNow, attempt, maxAttempts), CancellationToken.None);
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();
        }

        /// <summary>Another business in context, with a quote of its own.</summary>
        public async Task OtherBusinessAsync()
            => await Fixture.AddTenantAsync(BusinessType.Service, TenantCapability.Service | TenantCapability.Appointments, slug: "otro");

        /// <summary>A template for quotes of another business; this one stays in context.</summary>
        public async Task<Guid> OtherBusinessTemplateAsync()
        {
            var other = Guid.NewGuid();
            var template = new MessageTemplate(other, "Ajena", MessagePurpose.QuoteDocument, "Tu cotización {folio}: {enlace}");
            Context.MessageTemplates.Add(template);
            await Context.SaveChangesAsync();
            return template.Id;
        }

        public ValueTask DisposeAsync() => Fixture.DisposeAsync();
    }
}
