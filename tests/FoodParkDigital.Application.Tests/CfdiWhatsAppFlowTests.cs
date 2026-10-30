using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Customers.Commands;
using FoodParkDigital.Application.Invoicing;
using FoodParkDigital.Application.Messaging;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Domain.Messaging.Entities;
using FoodParkDigital.Domain.Messaging.Enums;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The PDF of each stamped CFDI of an order's invoice reaches its customer by WhatsApp, with a link to download its XML
/// (Fase 28M, ADR 0058): TienditApp queues it with the stamp when the invoice has a WhatsApp, a person sends one again within
/// limits, the queue sends it with the business's template or TienditApp's text, and the link opens only that CFDI.
/// </summary>
public class CfdiWhatsAppFlowTests
{
    private const string Rfc = "EKU9003173C9";
    private const string CustomerPhone = "+525512345678";
    private const string Karla = "+525598765432";
    private const string AppUrl = "https://tienditapp.mx";

    private static readonly CfdiReceiverInput Receiver = new()
    {
        Rfc = "xiqb891116qe4",
        Name = "Karla Fuente Nolasco",
        PostalCode = "26015",
        FiscalRegime = "612",
        CfdiUse = "G03"
    };

    [Fact]
    public async Task Stamp_ShouldQueueThePdf_ToTheInvoicesWhatsApp_WithTienditAppsText_AndItsLink()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidSaleAsync();

        var invoice = (await book.IssueAsync(sale.Id, whatsApp: "55 9876 5432")).Value;

        Assert.Equal((OrderInvoiceStatus.Stamped, Karla), (invoice.Status, invoice.ReceiverWhatsApp));
        var message = Assert.Single(await book.Context.OutgoingMessages.ToListAsync());
        Assert.Equal(
            (MessagePurpose.CfdiDocument, Karla, (CfdiEmailDocument?)CfdiEmailDocument.Invoice, (Guid?)invoice.Id, (Guid?)null, (string?)null),
            (message.Purpose, message.Phone, message.CfdiDocument, message.CfdiId, message.RequestedByUserId, message.ProviderTemplateName));
        Assert.StartsWith("Hola KARLA FUENTE NOLASCO: Negocio demo te envía la factura F-1 por $100.00, del ", message.Body);
        Assert.Matches(@", del \d{1,2} de [a-z]+ de \d{4}\. ", message.Body);
        Assert.EndsWith($". Descarga el XML y el PDF aquí: /factura#{message.LinkToken}", message.Body);
        Assert.Empty(message.GetParameters());

        var outcome = await book.SendQueuedAsync(message.Id);

        Assert.Equal(OutgoingSendDecision.Sent, outcome.Decision);
        var sent = Assert.Single(book.WhatsApp.Sent);
        var stamped = await book.Context.OrderInvoices.SingleAsync();
        Assert.Equal((book.Tenant.Id, Karla, $"F-1_{stamped.Uuid}.pdf", "application/pdf", (string?)null),
            (sent.TenantId, sent.Phone, sent.Document.FileName, sent.Document.ContentType, sent.TemplateName));
        // The PDF kept when it was stamped: the PAC is not asked again.
        Assert.Equal(book.Storage.Files[stamped.PdfFileKey!].Content, sent.Document.Content);
        Assert.Equal(1, book.Gateway.PdfRenders);
        // The link goes out with the app's address.
        Assert.EndsWith($"aquí: {AppUrl}/factura#{message.LinkToken}", sent.Caption);

        var shown = Assert.Single((await book.InvoicingAsync(sale.Id)).Invoices);
        Assert.Equal(Karla, shown.ReceiverWhatsApp);
        Assert.Equal((message.Id, true, Karla), (Assert.Single(shown.WhatsApps).Id, shown.WhatsApps[0].ByTienditApp, shown.WhatsApps[0].Phone));
    }

    [Fact]
    public async Task Template_ShouldCarryThePdfInItsHeader_AndItsParametersInTheOrderOfItsText()
    {
        await using var book = await Book.OpenAsync();
        var template = new MessageTemplate(book.Tenant.Id, "Factura", MessagePurpose.CfdiDocument,
            "{negocio} te manda {comprobante} ({monto}). XML: {enlace}. Gracias, {nombre}.", "factura_pdf");
        book.Context.MessageTemplates.Add(template);
        await book.Context.SaveChangesAsync();
        Assert.True((await book.SetTemplateAsync(template.Id)).IsSuccess);

        await book.IssueAsync((await book.PaidSaleAsync()).Id, whatsApp: Karla);
        var message = await book.Context.OutgoingMessages.SingleAsync();
        await book.SendQueuedAsync(message.Id);

        Assert.Equal(("factura_pdf", "es_MX"), (message.ProviderTemplateName, message.LanguageCode));
        Assert.Equal(["Negocio demo", "la factura F-1", "$100.00", $"/factura#{message.LinkToken}", "KARLA FUENTE NOLASCO"],
            message.GetParameters());
        var sent = Assert.Single(book.WhatsApp.Sent);
        Assert.Equal(("factura_pdf", "es_MX"), (sent.TemplateName, sent.LanguageCode));
        Assert.Equal($"{AppUrl}/factura#{message.LinkToken}", sent.Parameters[3]);

        // An inactive template goes back to TienditApp's text.
        (await book.Context.MessageTemplates.SingleAsync(t => t.Id == template.Id)).SetActive(false);
        await book.Context.SaveChangesAsync();
        await book.IssueAsync((await book.PaidSaleAsync()).Id, whatsApp: Karla);
        var next = await book.Context.OutgoingMessages.SingleAsync(m => m.ProviderTemplateName == null);
        Assert.Null(next.ProviderTemplateName);
        Assert.StartsWith("Hola KARLA FUENTE NOLASCO: Negocio demo te envía la factura F-2", next.Body);
    }

    [Fact]
    public async Task Stamp_ThatAnotherAttemptSavedFirst_ShouldLeaveOnlyThatAttemptsMessage()
    {
        var meanwhile = new StampedMeanwhile();
        await using var book = await Book.OpenAsync(interceptor: meanwhile);
        meanwhile.Tenant = book.Fixture.TenantService;
        meanwhile.Armed = true;

        var invoice = (await book.IssueAsync((await book.PaidSaleAsync()).Id, whatsApp: Karla)).Value;

        Assert.Equal(OrderInvoiceStatus.Stamped, invoice.Status);
        var message = Assert.Single(await book.Context.OutgoingMessages.ToListAsync());
        Assert.Equal(meanwhile.TheirsId, message.Id);
    }

    [Fact]
    public async Task Template_ThatComesOutTooLongForWhatsApp_ShouldGiveWayToTienditAppsText()
    {
        await using var book = await Book.OpenAsync();
        // Short enough to be saved; with the customer's name and the link, past what WhatsApp takes with the app's address.
        var template = new MessageTemplate(book.Tenant.Id, "Factura", MessagePurpose.CfdiDocument,
            new string('x', CfdiWhatsApps.MaxTextLength - 60) + " {nombre} {enlace}", "factura_pdf");
        book.Context.MessageTemplates.Add(template);
        await book.Context.SaveChangesAsync();
        Assert.True((await book.SetTemplateAsync(template.Id)).IsSuccess);

        await book.IssueAsync((await book.PaidSaleAsync()).Id, whatsApp: Karla);

        var message = await book.Context.OutgoingMessages.SingleAsync();
        Assert.Null(message.ProviderTemplateName);
        Assert.StartsWith("Hola KARLA FUENTE NOLASCO: Negocio demo te envía la factura F-1", message.Body);
    }

    [Fact]
    public async Task Stamp_ShouldQueueNothing_OnceTheBusinessesWhatsAppIsDisconnected()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.CreditSaleAsync(downPayment: 50m);
        await book.IssueAsync(sale.Id, whatsApp: Karla);
        await book.DisconnectWhatsAppAsync();

        // The complement is stamped all the same; its message would only fail, and count against the day's limit.
        Assert.Equal(1, (await book.IssuePendingComplementsAsync()).Value);
        Assert.Equal([CfdiEmailDocument.Invoice], await book.Context.OutgoingMessages.Select(m => m.CfdiDocument!.Value).ToListAsync());
    }

    [Fact]
    public async Task ComplementsAndCreditNotes_ShouldGoToTheWhatsAppTheirInvoiceHas_WhenTheyAreStamped()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.CreditSaleAsync(downPayment: 50m);
        var invoice = (await book.IssueAsync(sale.Id, whatsApp: Karla)).Value;
        Assert.Equal(1, (await book.IssuePendingComplementsAsync()).Value);
        await book.WriteOffAsync(sale.Id, 50m);
        Assert.Equal(1, (await book.IssuePendingCreditNotesAsync()).Value);

        var complement = await book.Context.PaymentComplements.SingleAsync();
        var note = await book.Context.CreditNotes.SingleAsync();
        var messages = await book.Context.OutgoingMessages.OrderBy(m => m.CfdiDocument).ToListAsync();
        Assert.Equal(
            [
                (CfdiEmailDocument.Invoice, invoice.Id, Karla),
                (CfdiEmailDocument.PaymentComplement, complement.Id, Karla),
                (CfdiEmailDocument.CreditNote, note.Id, Karla),
            ],
            messages.Select(m => (m.CfdiDocument!.Value, m.CfdiId!.Value, m.Phone)));
        Assert.All(messages, m => Assert.Equal(invoice.Id, m.OrderInvoiceId));
        Assert.Contains("el complemento de pago P-1 por $50.00", messages[1].Body);
        Assert.Contains("la nota de crédito NC-1 por $50.00", messages[2].Body);

        var invoicing = await book.InvoicingAsync(sale.Id);
        Assert.Single(invoicing.Payments.SelectMany(p => p.Complements).Single().WhatsApps);
        Assert.Single(invoicing.Adjustments.SelectMany(a => a.CreditNotes).Single().WhatsApps);
    }

    [Fact]
    public async Task Issue_WithAWhatsApp_ShouldNeedTheBusinessesWhatsAppConnected_AndANumber()
    {
        await using var book = await Book.OpenAsync(connected: false);
        var sale = await book.PaidSaleAsync();

        Assert.Equal(CfdiWhatsApps.NotConnectedMessage, (await book.IssueAsync(sale.Id, whatsApp: Karla)).Error);
        await book.ConnectWhatsAppAsync();
        Assert.Equal(InvoiceWhatsApp.InvalidMessage, (await book.IssueAsync(sale.Id, whatsApp: "12345")).Error);
        Assert.Empty(await book.Context.OrderInvoices.ToListAsync());

        // Without one, nothing goes by WhatsApp.
        Assert.Null((await book.IssueAsync(sale.Id)).Value.ReceiverWhatsApp);
        Assert.Empty(await book.Context.OutgoingMessages.ToListAsync());
    }

    [Fact]
    public async Task Invoicing_ShouldProposeTheLastInvoicesWhatsApp_ThenTheOrdersPhone_OnlyWhileConnected()
    {
        await using var book = await Book.OpenAsync(connected: false);
        var sale = await book.PaidSaleAsync();
        var invoicing = await book.InvoicingAsync(sale.Id);
        Assert.Equal((false, (string?)null), (invoicing.WhatsAppConnected, invoicing.ProposedWhatsApp));

        await book.ConnectWhatsAppAsync();
        invoicing = await book.InvoicingAsync(sale.Id);
        Assert.Equal((true, CustomerPhone), (invoicing.WhatsAppConnected, invoicing.ProposedWhatsApp));

        var invoice = (await book.IssueAsync(sale.Id, whatsApp: Karla)).Value;
        await book.CancelInvoiceAsync(invoice.Id);
        Assert.Equal(Karla, (await book.InvoicingAsync(sale.Id)).ProposedWhatsApp);
    }

    [Fact]
    public async Task Send_ShouldQueueAnotherOne_ToTheNumberGiven_AndKeepItForWhatFollows_WhenAsked()
    {
        await using var book = await Book.OpenAsync();
        var invoice = (await book.IssueAsync((await book.PaidSaleAsync()).Id)).Value;

        var sent = await book.SendAsync(CfdiEmailDocument.Invoice, invoice.Id, "55 9876 5432");
        Assert.True(sent.IsSuccess, sent.Error);
        Assert.Equal((Karla, false, OutgoingMessageStatus.Pending), (sent.Value.Phone, sent.Value.ByTienditApp, sent.Value.Status));
        Assert.Null((await book.Context.OrderInvoices.SingleAsync()).ReceiverWhatsApp);
        var message = await book.Context.OutgoingMessages.SingleAsync();
        Assert.Equal(Guid.Parse(book.Fixture.CurrentUser.UserId!), message.RequestedByUserId);

        // Kept for what comes next of the invoice, international numbers included.
        Assert.True((await book.SendAsync(CfdiEmailDocument.Invoice, invoice.Id, "+1 415 555 0123", useForInvoice: true)).IsSuccess);
        Assert.Equal("+14155550123", (await book.Context.OrderInvoices.SingleAsync()).ReceiverWhatsApp);
        // Each one with its own link.
        Assert.Equal(2, (await book.Context.OutgoingMessages.Select(m => m.LinkToken).Distinct().ToListAsync()).Count);
    }

    [Fact]
    public async Task Send_ShouldOnlyTakeAStampedCfdi_OfTheBusiness_ToANumber_WithItsWhatsAppConnected()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidSaleAsync();
        book.Gateway.Unavailable = true;
        var stamping = (await book.IssueAsync(sale.Id)).Value;
        book.Gateway.Unavailable = false;
        Assert.Equal(OrderInvoiceStatus.Stamping, stamping.Status);

        Assert.Equal(OutgoingMessage.CfdiNotStampedMessage, (await book.SendAsync(CfdiEmailDocument.Invoice, stamping.Id, Karla)).Error);
        await Assert.ThrowsAsync<EntityNotFoundException>(() => book.SendAsync(CfdiEmailDocument.Invoice, Guid.NewGuid(), Karla));
        // The id of an invoice is not a complement's.
        await Assert.ThrowsAsync<EntityNotFoundException>(() => book.SendAsync(CfdiEmailDocument.PaymentComplement, stamping.Id, Karla));

        // Another business's stamped invoice is not there for this one.
        var theirs = await book.TheirStampedInvoiceAsync();
        await Assert.ThrowsAsync<EntityNotFoundException>(() => book.SendAsync(CfdiEmailDocument.Invoice, theirs.Id, Karla));

        var stamped = (await book.RetryAsync(stamping.Id)).Value;
        Assert.Equal(InvoiceWhatsApp.InvalidMessage, (await book.SendAsync(CfdiEmailDocument.Invoice, stamped.Id, "55 1234")).Error);
        await book.DisconnectWhatsAppAsync();
        Assert.Equal(CfdiWhatsApps.NotConnectedMessage, (await book.SendAsync(CfdiEmailDocument.Invoice, stamped.Id, Karla)).Error);
        Assert.Empty(await book.Context.OutgoingMessages.IgnoreQueryFilters().ToListAsync());

        var validator = new SendCfdiByWhatsAppCommandValidator();
        Assert.Equal("Escribe el WhatsApp al que se envía.",
            Assert.Single(validator.Validate(new SendCfdiByWhatsAppCommand(CfdiEmailDocument.Invoice, stamped.Id, " ", false)).Errors).ErrorMessage);
        Assert.False(validator.Validate(new SendCfdiByWhatsAppCommand((CfdiEmailDocument)7, stamped.Id, Karla, false)).IsValid);
    }

    [Fact]
    public async Task Send_ShouldStopAtFiveForOneCfdi_AndAtThreeHundredForTheBusiness_InADay()
    {
        await using var book = await Book.OpenAsync();
        var invoice = (await book.IssueAsync((await book.PaidSaleAsync()).Id, whatsApp: Karla)).Value;

        // The one TienditApp queued counts.
        for (var i = 0; i < CfdiWhatsApps.MaxPerCfdiPerDay - 1; i++)
        {
            book.Clock.UtcNow = book.Clock.UtcNow.AddMinutes(1);
            Assert.True((await book.SendAsync(CfdiEmailDocument.Invoice, invoice.Id, Karla)).IsSuccess);
        }
        Assert.Equal(CfdiWhatsApps.TooManyForCfdiMessage("F-1"), (await book.SendAsync(CfdiEmailDocument.Invoice, invoice.Id, Karla)).Error);
        Assert.Equal(CfdiWhatsApps.MaxPerCfdiPerDay, await book.Context.OutgoingMessages.CountAsync());

        // A day after the first, one more goes.
        book.Clock.UtcNow = book.Clock.UtcNow.AddDays(1).AddMinutes(-3);
        Assert.True((await book.SendAsync(CfdiEmailDocument.Invoice, invoice.Id, Karla)).IsSuccess);

        // The business's day: another CFDI is not sent either. Other messages of the queue do not count.
        var stamped = await book.Context.OrderInvoices.AsNoTracking().SingleAsync();
        book.Context.OutgoingMessages.Add(new OutgoingMessage(
            book.Tenant.Id, MessagePurpose.AppointmentNotice, Karla, null, "Tu cita", null, "es_MX", [], book.Clock.UtcNow));
        for (var i = await book.Context.OutgoingMessages.CountAsync(m => m.Purpose == MessagePurpose.CfdiDocument && m.CreatedAt > book.Clock.UtcNow - CfdiEmails.Day);
             i < CfdiWhatsApps.MaxPerBusinessPerDay; i++)
            book.Context.OutgoingMessages.Add(OutgoingMessage.ForCfdi(
                stamped, Karla, null, "Hola", null, "es_MX", [], OrderTracking.NewToken(), null, book.Clock.UtcNow));
        await book.Context.SaveChangesAsync();
        var other = (await book.IssueAsync((await book.PaidSaleAsync()).Id)).Value;
        Assert.Equal(CfdiWhatsApps.TooManyForBusinessMessage, (await book.SendAsync(CfdiEmailDocument.Invoice, other.Id, Karla)).Error);
    }

    [Fact]
    public async Task Sender_ShouldWaitForThePdf_AskThePacForIt_AndRetryWhatWhatsAppMayTakeLater()
    {
        await using var book = await Book.OpenAsync();
        book.Gateway.PdfUnavailable = true;
        await book.IssueAsync((await book.PaidSaleAsync()).Id, whatsApp: Karla);
        var message = await book.Context.OutgoingMessages.SingleAsync();
        Assert.Null((await book.Context.OrderInvoices.SingleAsync()).PdfFileKey);

        // Stamped a moment ago: each try waits for the PDF the stamp keeps, without asking the PAC nor counting as a failed try.
        var renders = book.Gateway.PdfRequests;
        var waiting = await book.SendQueuedAsync(message.Id);
        Assert.Equal((OutgoingSendDecision.Postponed, CfdiWhatsAppSender.PdfPendingReason), Decision(waiting));
        Assert.InRange(waiting.RetryAfter!.Value, TimeSpan.FromSeconds(1), CfdiWhatsAppSender.PdfWait);
        book.Clock.UtcNow = book.Clock.UtcNow.Add(CfdiWhatsAppSender.PdfWait - TimeSpan.FromSeconds(1));
        Assert.Equal(OutgoingSendDecision.Postponed, (await book.SendQueuedAsync(message.Id)).Decision);
        Assert.Equal(renders, book.Gateway.PdfRequests);

        // Two minutes after it was queued the PAC renders it; without it, the queue tries again.
        book.Clock.UtcNow = book.Clock.UtcNow.AddSeconds(1);
        Assert.Equal((OutgoingSendDecision.TransientFailure, CfdiWhatsAppSender.PdfUnavailableReason), Decision(await book.SendQueuedAsync(message.Id)));
        book.Gateway.PdfUnavailable = false;
        book.WhatsApp.Next = MessageSendResult.Failed("Error al enviar mensaje de WhatsApp: TooManyRequests", isTransient: true);
        Assert.Equal(OutgoingSendDecision.TransientFailure, (await book.SendQueuedAsync(message.Id)).Decision);
        book.WhatsApp.Next = MessageSendResult.Failed("Error al enviar mensaje de WhatsApp: BadRequest", isTransient: false);
        Assert.Equal(OutgoingSendDecision.Rejected, (await book.SendQueuedAsync(message.Id)).Decision);
        book.WhatsApp.Next = null;
        var sent = await book.SendQueuedAsync(message.Id);
        Assert.Equal((OutgoingSendDecision.Sent, "wamid.cfdi"), (sent.Decision, sent.ProviderMessageId));
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(book.WhatsApp.Sent[^1].Document.Content));
    }

    [Fact]
    public async Task Sender_ShouldNotSendACfdiThatNoLongerStands_ButSendOneToACustomerWhoOptedOut()
    {
        await using var book = await Book.OpenAsync();
        var invoice = (await book.IssueAsync((await book.PaidSaleAsync()).Id, whatsApp: Karla)).Value;
        var message = await book.Context.OutgoingMessages.SingleAsync();

        // BAJA stops promotions and reminders; the receipt of a purchase the customer asked to invoice still goes.
        book.Context.MessagingOptOuts.Add(new MessagingOptOut(book.Tenant.Id, Karla, OptOutSource.CustomerKeyword, book.Clock.UtcNow));
        await book.Context.SaveChangesAsync();
        Assert.Equal(OutgoingSendDecision.Sent, (await book.SendQueuedAsync(message.Id)).Decision);

        await book.CancelInvoiceAsync(invoice.Id);
        Assert.Equal((OutgoingSendDecision.Skipped, CfdiWhatsAppSender.NoLongerStampedReason), Decision(await book.SendQueuedAsync(message.Id)));
    }

    [Fact]
    public async Task Link_ShouldOpenItsCfdi_ForNinetyDays_WhileItStands_AndNothingElse()
    {
        await using var book = await Book.OpenAsync();
        var invoice = (await book.IssueAsync((await book.PaidSaleAsync()).Id, whatsApp: Karla)).Value;
        var token = (await book.Context.OutgoingMessages.SingleAsync()).LinkToken!;
        var theirs = await book.TheirStampedInvoiceAsync();
        var theirToken = await book.TheirMessageAsync(theirs);

        // Asked from any business in scope, each link opens its own CFDI.
        var page = await book.PublicAsync(token);
        Assert.Equal(("Negocio demo", "F-1", invoice.Uuid, 100m, true, "KARLA FUENTE NOLASCO", "XIQB891116QE4"),
            (page!.BusinessName, page.Number, page.Uuid, page.Amount, page.Stands, page.ReceiverName, page.ReceiverRfc));
        Assert.Equal(book.Clock.UtcNow.AddDays(90), page.LinkExpiresAt);
        Assert.Equal(theirs.Uuid, (await book.PublicAsync(theirToken))!.Uuid);
        var xml = await book.PublicFileAsync(token, OrderInvoiceFileKind.Xml);
        Assert.True(xml.IsSuccess, xml.Error);
        Assert.Equal($"F-1_{invoice.Uuid}.xml", xml.Value.FileName);

        // A link of another shape, unknown, or changed by a letter finds nothing.
        foreach (var wrong in new[] { "corto", new string('A', 43), token[..42] + (token[42] == 'A' ? 'B' : 'A'), token.ToLowerInvariant() })
            if (wrong != token)
                Assert.Null(await book.PublicAsync(wrong));

        // A business that is no longer active: its links find nothing.
        await book.SuspendTheirsAsync();
        Assert.Null(await book.PublicAsync(theirToken));

        // Once its cancellation is asked for, it says so and is no longer downloaded.
        await book.CancelInvoiceAsync(invoice.Id);
        Assert.False((await book.PublicAsync(token))!.Stands);
        Assert.Equal(GetPublicCfdiFileQueryHandler.NotStandingMessage, (await book.PublicFileAsync(token, OrderInvoiceFileKind.Pdf)).Error);

        // Ninety days after it was queued, the link expires.
        book.Clock.UtcNow = book.Clock.UtcNow.AddDays(90);
        Assert.Null(await book.PublicAsync(token));
        Assert.Equal(PublicCfdiLinks.NotFoundMessage, (await book.PublicFileAsync(token, OrderInvoiceFileKind.Xml)).Error);
    }

    [Fact]
    public async Task Settings_ShouldChooseOneOfTheBusinessesActiveTemplatesForInvoices_OrTienditAppsText()
    {
        await using var book = await Book.OpenAsync();
        var mine = new MessageTemplate(book.Tenant.Id, "Factura", MessagePurpose.CfdiDocument, "Hola {nombre}: {enlace}", "factura_pdf");
        var campaign = new MessageTemplate(book.Tenant.Id, "Promo", MessagePurpose.Campaign, "Hola {nombre}");
        var inactive = new MessageTemplate(book.Tenant.Id, "Vieja", MessagePurpose.CfdiDocument, "Hola {nombre}: {enlace}");
        inactive.SetActive(false);
        var theirTenant = await book.Fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, slug: "otro");
        var theirs = new MessageTemplate(theirTenant.Id, "Factura de otro", MessagePurpose.CfdiDocument, "Hola {nombre}: {enlace}");
        book.Fixture.TenantService.SetTenant(book.Tenant.Id);
        book.Context.MessageTemplates.AddRange(mine, campaign, inactive, theirs);
        await book.Context.SaveChangesAsync();

        foreach (var templateId in new[] { campaign.Id, inactive.Id, theirs.Id, Guid.NewGuid() })
            Assert.Equal(SetInvoiceWhatsAppTemplateCommandHandler.NotATemplateMessage, (await book.SetTemplateAsync(templateId)).Error);

        var settings = (await book.SetTemplateAsync(mine.Id)).Value;
        Assert.Equal((mine.Id, true), (settings.WhatsAppTemplateId, settings.WhatsAppConnected));
        Assert.Equal([("Factura", "factura_pdf")], settings.WhatsAppTemplates.Select(t => (t.Name, t.ProviderTemplateName)));
        Assert.Equal(CfdiWhatsApps.DefaultText, settings.WhatsAppDefaultText);

        // Deactivated afterwards, the settings say TienditApp's text is the one that goes.
        (await book.Context.MessageTemplates.SingleAsync(t => t.Id == mine.Id)).SetActive(false);
        await book.Context.SaveChangesAsync();
        Assert.Null((await book.SettingsAsync()).WhatsAppTemplateId);
        Assert.Null((await book.SetTemplateAsync(null)).Value.WhatsAppTemplateId);
    }

    private static (OutgoingSendDecision, string?) Decision(OutgoingSendOutcome outcome) => (outcome.Decision, outcome.Reason);

    /// <summary>
    /// The next save of a stamp finds that another attempt (the worker, with its own context) stamped the same invoice and
    /// queued its WhatsApp first: the database refuses this one as stale, with its message.
    /// </summary>
    private sealed class StampedMeanwhile : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public ICurrentTenantService? Tenant { get; set; }
        public Guid TheirsId { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var stamping = eventData.Context!.ChangeTracker.Entries<OrderInvoice>()
                .FirstOrDefault(entry => entry.State == EntityState.Modified && entry.Entity.Status == OrderInvoiceStatus.Stamped)?.Entity;
            if (!Armed || stamping is null)
                return result;
            Armed = false;

            await using var other = new SalesDbContext(
                (DbContextOptions<ApplicationDbContext>)eventData.Context.GetService<IDbContextOptions>(), Tenant!);
            var theirs = await other.OrderInvoices.SingleAsync(i => i.Id == stamping.Id, cancellationToken);
            theirs.Stamp(stamping.Uuid!, stamping.Uuid!, stamping.StampedAt!.Value, stamping.XmlFileKey!);
            var message = OutgoingMessage.ForCfdi(
                theirs, theirs.ReceiverWhatsApp, null, "Hola", null, "es_MX", [], OrderTracking.NewToken(), null, stamping.StampedAt.Value);
            other.OutgoingMessages.Add(message);
            await other.SaveChangesAsync(cancellationToken);
            TheirsId = message.Id;
            throw new DbUpdateConcurrencyException("The invoice was stamped meanwhile.");
        }
    }

    private sealed class RecordingDocuments : IWhatsAppDocumentSender
    {
        public List<(Guid TenantId, string Phone, WhatsAppDocument Document, string Caption, string? TemplateName, string LanguageCode, string[] Parameters)> Sent { get; } = [];
        public MessageSendResult? Next { get; set; }

        public Task<MessageSendResult> SendDocumentAsync(
            Guid tenantId, string phoneNumber, WhatsAppDocument document, string caption, string? templateName, string languageCode,
            string[] parameters, CancellationToken cancellationToken = default)
        {
            if (Next is { } next)
                return Task.FromResult(next);
            Sent.Add((tenantId, phoneNumber, document, caption, templateName, languageCode, parameters));
            return Task.FromResult(MessageSendResult.Sent("wamid.cfdi"));
        }
    }

    private sealed class NoWhatsApp : IWhatsAppService
    {
        public Task<MessageSendResult> SendTemplateMessageAsync(
            Guid tenantId, string phoneNumber, string templateName, string languageCode, string[] parameters, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("A CFDI goes as a document.");

        public Task<MessageSendResult> SendTextMessageAsync(Guid tenantId, string phoneNumber, string message, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("A CFDI goes as a document.");
    }

    private sealed class Links : IAppLinks
    {
        public string? BaseUrl => AppUrl;
    }

    private sealed class Book : IAsyncDisposable, ISender
    {
        private Book(SalesTestFixture fixture, Tenant tenant, Product product)
        {
            Fixture = fixture;
            Tenant = tenant;
            Product = product;
        }

        public SalesTestFixture Fixture { get; }
        public Tenant Tenant { get; }
        public Product Product { get; }
        public Tenant? Theirs { get; private set; }
        public FakeInvoicingGateway Gateway { get; } = new();
        public MemoryPrivateStorage Storage { get; } = new();
        public RecordingDocuments WhatsApp { get; } = new();
        public SalesDbContext Context => Fixture.Context;
        public SalesClock Clock => Fixture.Clock;

        public static async Task<Book> OpenAsync(bool connected = true, IInterceptor? interceptor = null)
        {
            var fixture = interceptor is null ? SalesTestFixture.Create() : SalesTestFixture.Create(null, interceptor);
            var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
            await fixture.OpenCashSessionAsync(tenant);
            var customer = new Customer(tenant.Id, "Karla", CustomerPhone, null);
            customer.SetCreditLimit(1_000m);
            fixture.Context.Customers.Add(customer);
            tenant.SetBusinessInfo(
                Rfc, "ESCUELA KEMPER URGATE", "Comercio", "601 - General de Ley Personas Morales",
                new Address("Reforma", "1", null, "Centro", "06000", "Cuauhtémoc", "Ciudad de México"));
            await fixture.Context.SaveChangesAsync();
            var book = new Book(fixture, tenant, await fixture.AddProductAsync(tenant, "Café", 100m));
            var (cer, key) = TestCsd.Make(uniqueIdentifier: $"{Rfc} / VADA800927HSRSRL05");
            var uploaded = await new UploadInvoicingCertificateCommandHandler(
                    book.Context, fixture.TenantService, fixture.CurrentUser, book.Gateway, book.Clock,
                    NullLogger<UploadInvoicingCertificateCommandHandler>.Instance)
                .Handle(new UploadInvoicingCertificateCommand(Convert.ToBase64String(cer), Convert.ToBase64String(key), TestCsd.Password),
                    CancellationToken.None);
            Assert.True(uploaded.IsSuccess, uploaded.Error);
            if (connected)
                await book.ConnectWhatsAppAsync();
            return book;
        }

        public async Task ConnectWhatsAppAsync()
        {
            var config = await Context.WhatsAppConfigs.FirstOrDefaultAsync(w => w.TenantId == Tenant.Id);
            if (config is null)
            {
                config = new WhatsAppConfig(Tenant.Id);
                Context.WhatsAppConfigs.Add(config);
            }
            config.Update(isEnabled: true, accessToken: "token", phoneNumberId: "2233445566", wabaId: "1122334455", displayPhoneNumber: null);
            await Context.SaveChangesAsync();
        }

        public async Task DisconnectWhatsAppAsync()
        {
            var config = await Context.WhatsAppConfigs.SingleAsync(w => w.TenantId == Tenant.Id);
            config.Update(isEnabled: false, accessToken: "token", phoneNumberId: "2233445566", wabaId: "1122334455", displayPhoneNumber: null);
            await Context.SaveChangesAsync();
        }

        /// <summary>A $100 sale to Karla, paid by card.</summary>
        public async Task<OrderDto> PaidSaleAsync()
        {
            var created = await Fixture.CreateOrderHandler().Handle(new CreateOrderCommand
            {
                CustomerName = "Karla",
                CustomerPhone = CustomerPhone,
                Items = [new CreateOrderItemDto { ProductId = Product.Id, Quantity = 1 }],
                Payments = [new CreateOrderPaymentDto { Amount = 100m, Method = OrderPaymentMethod.Card }]
            }, CancellationToken.None);
            Assert.True(created.IsSuccess, created.Error);
            return created.Value;
        }

        /// <summary>A $200 sale on credit to Karla (2 cafés, IVA included), with what she paid at the counter.</summary>
        public async Task<OrderDto> CreditSaleAsync(decimal downPayment)
        {
            var customer = await Context.Customers.SingleAsync();
            var created = await Fixture.CreateOrderHandler().Handle(new CreateOrderCommand
            {
                CustomerId = customer.Id,
                OnCredit = true,
                Items = [new CreateOrderItemDto { ProductId = Product.Id, Quantity = 2 }],
                Payments = [new CreateOrderPaymentDto { Amount = downPayment, Method = OrderPaymentMethod.Cash }]
            }, CancellationToken.None);
            Assert.True(created.IsSuccess, created.Error);
            return created.Value;
        }

        public async Task WriteOffAsync(Guid orderId, decimal amount)
        {
            Clock.UtcNow = Clock.UtcNow.AddMinutes(1);
            var order = await Context.Orders.Include(o => o.Payments).Include(o => o.WriteOffs).SingleAsync(o => o.Id == orderId);
            order.WriteOff(amount, OrderWriteOffReason.WontBeCollected, null, Clock.UtcNow, "dueña");
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();
        }

        public Task<OrderInvoicingDto> InvoicingAsync(Guid orderId)
            => new GetOrderInvoicingQueryHandler(Context, Fixture.TenantService, Gateway, new PhoneNormalizationService(), Clock)
                .Handle(new GetOrderInvoicingQuery(orderId), CancellationToken.None);

        public async Task<Result<OrderInvoiceDto>> IssueAsync(Guid orderId, string? whatsApp = null)
        {
            Clock.UtcNow = Clock.UtcNow.AddSeconds(1);
            var issued = await new IssueOrderInvoiceCommandHandler(this, Context, Gateway, Storage, Clock, NullLogger<OrderInvoiceStamper>.Instance)
                .Handle(new IssueOrderInvoiceCommand(orderId, Receiver, false, null, whatsApp), CancellationToken.None);
            Context.ChangeTracker.Clear();
            return issued;
        }

        public Task<Result<OrderInvoiceDto>> RetryAsync(Guid invoiceId)
            => new RetryOrderInvoiceCommandHandler(Context, Gateway, Storage, Clock, NullLogger<OrderInvoiceStamper>.Instance)
                .Handle(new RetryOrderInvoiceCommand(invoiceId), CancellationToken.None);

        public async Task CancelInvoiceAsync(Guid invoiceId)
        {
            var cancelled = await new CancelOrderInvoiceCommandHandler(Context, Fixture.CurrentUser, Gateway, Clock)
                .Handle(new CancelOrderInvoiceCommand(invoiceId, CfdiCancellationMotive.ErrorsWithoutReplacement), CancellationToken.None);
            Assert.True(cancelled.IsSuccess, cancelled.Error);
            Context.ChangeTracker.Clear();
        }

        public Task<Result<int>> IssuePendingComplementsAsync()
            => new IssuePendingPaymentComplementsCommandHandler(this, Context, Gateway, Storage, Clock, NullLogger<OrderInvoiceStamper>.Instance)
                .Handle(new IssuePendingPaymentComplementsCommand(), CancellationToken.None);

        public Task<Result<int>> IssuePendingCreditNotesAsync()
            => new IssuePendingCreditNotesCommandHandler(this, Context, Gateway, Storage, Clock, NullLogger<OrderInvoiceStamper>.Instance)
                .Handle(new IssuePendingCreditNotesCommand(), CancellationToken.None);

        public async Task<Result<CfdiWhatsAppDto>> SendAsync(CfdiEmailDocument document, Guid cfdiId, string phone, bool useForInvoice = false)
        {
            var sent = await new SendCfdiByWhatsAppCommandHandler(
                    Context, Fixture.CurrentUser, new PhoneNormalizationService(), new InvoicingLock(Context), Clock)
                .Handle(new SendCfdiByWhatsAppCommand(document, cfdiId, phone, useForInvoice), CancellationToken.None);
            Context.ChangeTracker.Clear();
            return sent;
        }

        /// <summary>The messaging worker sends a queued message in its business's scope.</summary>
        public async Task<OutgoingSendOutcome> SendQueuedAsync(Guid messageId)
        {
            Context.ChangeTracker.Clear();
            var message = await Context.OutgoingMessages.AsNoTracking().SingleAsync(m => m.Id == messageId);
            var cfdi = new CfdiWhatsAppSender(Context, Storage, Gateway, WhatsApp, new Links(), Clock, NullLogger<CfdiWhatsAppSender>.Instance);
            var sender = new OutgoingMessageSender(
                Context, new NoWhatsApp(), new MessagingConsentService(Context, Fixture.TenantService, Clock), Clock, cfdi);
            return await sender.SendAsync(message, CancellationToken.None);
        }

        public Task<Result<InvoicingSettingsDto>> SetTemplateAsync(Guid? templateId)
            => new SetInvoiceWhatsAppTemplateCommandHandler(Context, Fixture.TenantService, Fixture.CurrentUser, Gateway, Clock)
                .Handle(new SetInvoiceWhatsAppTemplateCommand(templateId), CancellationToken.None);

        public Task<InvoicingSettingsDto> SettingsAsync()
            => new GetInvoicingSettingsQueryHandler(Context, Fixture.TenantService, Gateway, Clock)
                .Handle(new GetInvoicingSettingsQuery(), CancellationToken.None);

        /// <summary>A stamped invoice of a second business, written in its own scope; the first is back in scope after it.</summary>
        public async Task<OrderInvoice> TheirStampedInvoiceAsync()
        {
            // The CFDI of the first business's invoice, as if the second one had issued the same.
            var document = (await Context.OrderInvoices.AsNoTracking().FirstAsync()).Document;
            Theirs = await Fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, slug: "otro");
            var invoice = OrderInvoice.Request(
                Theirs.Id, Guid.NewGuid(), "A", 1, CfdiReceiver.Create("XIQB891116QE4", "Karla Fuente Nolasco", "26015", "612", "G03"),
                "04", document, 86.21m, 13.79m, 100m, Guid.NewGuid(), Clock.UtcNow);
            invoice.Stamp(Guid.NewGuid().ToString(), "pac-b", Clock.UtcNow, "invoices/b.xml");
            await Storage.SaveAsync("invoices/b.xml", new MemoryStream("<cfdi>b</cfdi>"u8.ToArray()), "application/xml");
            Context.OrderInvoices.Add(invoice);
            await Context.SaveChangesAsync();
            Fixture.TenantService.SetTenant(Tenant.Id);
            Context.ChangeTracker.Clear();
            return invoice;
        }

        /// <summary>A message of the second business's invoice, queued in its scope: its link.</summary>
        public async Task<string> TheirMessageAsync(OrderInvoice invoice)
        {
            Fixture.TenantService.SetTenant(invoice.TenantId);
            var message = OutgoingMessage.ForCfdi(invoice, Karla, null, "Hola", null, "es_MX", [], OrderTracking.NewToken(), null, Clock.UtcNow);
            Context.OutgoingMessages.Add(message);
            await Context.SaveChangesAsync();
            Fixture.TenantService.SetTenant(Tenant.Id);
            Context.ChangeTracker.Clear();
            return message.LinkToken!;
        }

        public async Task SuspendTheirsAsync()
        {
            var theirs = await Context.Tenants.SingleAsync(t => t.Id == Theirs!.Id);
            theirs.SetStatus(TenantStatus.Suspended);
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();
        }

        /// <summary>The public link, read with the first business in scope, as a request without an account would not be.</summary>
        public async Task<PublicCfdiDto?> PublicAsync(string token)
        {
            Context.ChangeTracker.Clear();
            var page = await new GetPublicCfdiQueryHandler(Context, Fixture.TenantService, Clock)
                .Handle(new GetPublicCfdiQuery(token), CancellationToken.None);
            Fixture.TenantService.SetTenant(Tenant.Id);
            return page;
        }

        public async Task<Result<PrivateFileDownload>> PublicFileAsync(string token, OrderInvoiceFileKind kind)
        {
            Context.ChangeTracker.Clear();
            var file = await new GetPublicCfdiFileQueryHandler(
                    Context, Fixture.TenantService, Storage, Gateway, Clock, NullLogger<GetPublicCfdiFileQueryHandler>.Instance)
                .Handle(new GetPublicCfdiFileQuery(token, kind), CancellationToken.None);
            Fixture.TenantService.SetTenant(Tenant.Id);
            return file;
        }

        public ValueTask DisposeAsync() => Fixture.DisposeAsync();

        // The requests go to their own handlers, as MediatR would send them.
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            object response = request switch
            {
                RequestOrderInvoiceCommand command => await new RequestOrderInvoiceCommandHandler(
                        Context, Fixture.TenantService, Fixture.CurrentUser, new InvoicingLock(Context), new FolioGenerator(Context),
                        new PhoneNormalizationService(), Clock)
                    .Handle(command, cancellationToken),
                RequestAutomaticPaymentComplementCommand command => await new RequestAutomaticPaymentComplementCommandHandler(
                        Context, Fixture.TenantService, Fixture.CurrentUser, Gateway, new InvoicingLock(Context), new FolioGenerator(Context), Clock)
                    .Handle(command, cancellationToken),
                RequestAutomaticCreditNoteCommand command => await new RequestAutomaticCreditNoteCommandHandler(
                        Context, Fixture.TenantService, Fixture.CurrentUser, Gateway, new InvoicingLock(Context), new FolioGenerator(Context), Clock)
                    .Handle(command, cancellationToken),
                _ => throw new NotSupportedException(request.GetType().Name)
            };
            return (TResponse)response;
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
            => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
