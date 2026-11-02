using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Messaging;
using FoodParkDigital.Domain.Messaging.Entities;
using FoodParkDigital.Domain.Messaging.Enums;
using FoodParkDigital.Domain.Quotes;
using FoodParkDigital.Domain.Quotes.Entities;
using FoodParkDigital.Domain.Quotes.Events;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// The quote reaches the customer from TienditApp (Fase 29D, ADR 0053 point 16): an email with its outcome, and a WhatsApp
/// with the business's template or TienditApp's text, both only of a version sent and still valid, and both carrying its link.
/// </summary>
public class QuoteDeliveryTests
{
    private static readonly Guid Business = Guid.NewGuid();
    private static readonly Guid Staff = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 2);
    private static readonly QuoteCustomer Valeria = new(null, "Valeria Ruiz", "5522107765", "valeria@correo.mx");

    private static Quote Draft()
    {
        var quote = Quote.Start(Business, Folios.QuoteNumber(7), Valeria, null, true, 15, Now);
        quote.ReplaceLines([new QuoteLineInput(null, "Cambio de calentador", 1m, 1160m, VatRate.General)], Now);
        return quote;
    }

    private static Quote Sent()
    {
        var quote = Draft();
        quote.Send(Today, new string('a', 43), null, Now);
        return quote;
    }

    [Fact]
    public void AnEmail_ShouldCarryASentVersion_ToItsAddress_AndGoOutFromTheOutbox()
    {
        var quote = Sent();

        var email = QuoteEmail.Request(quote, "  valeria@correo.mx ", Staff, Today, Now);

        Assert.Equal((Business, quote.Id, "valeria@correo.mx", Staff, Now), (email.TenantId, email.QuoteId, email.To, email.RequestedByUserId, email.RequestedAt));
        Assert.True(email.IsPending);
        Assert.Equal(email.Id, Assert.IsType<QuoteEmailRequestedEvent>(Assert.Single(email.DomainEvents)).QuoteEmailId);
    }

    [Fact]
    public void OnlyAVersionSentAndValid_ShouldGoToTheCustomer()
    {
        var accepted = Sent();
        accepted.Accept(Today, QuoteReplyChannel.WhatsApp, null, Now);
        var replaced = Sent();
        var correction = replaced.Revise(2, Now);
        correction.Send(Today, new string('b', 43), replaced, Now);

        foreach (var (quote, today) in new[] { (Draft(), Today), (Sent(), Today.AddDays(16)), (accepted, Today), (replaced, Today) })
        {
            Assert.Equal(QuoteRules.NotSendableMessage,
                Assert.Throws<DomainException>(() => QuoteEmail.Request(quote, "valeria@correo.mx", Staff, today, Now)).Message);
            Assert.Equal(QuoteRules.NotSendableMessage,
                Assert.Throws<DomainException>(() => OutgoingMessage.ForQuote(quote, "+525522107765", "Valeria", "Hola", null, "es_MX", [], Staff, today, Now)).Message);
        }
        // The last day of its validity it still goes.
        Assert.True(QuoteEmail.Request(Sent(), "valeria@correo.mx", Staff, Today.AddDays(15), Now).IsPending);
    }

    [Theory]
    [InlineData(null, QuoteRules.EmailMessage)]
    [InlineData("  ", QuoteRules.EmailMessage)]
    [InlineData("Valeria <valeria@correo.mx>", InvoiceEmailAddress.InvalidMessage)]
    [InlineData("peña@correo.mx", InvoiceEmailAddress.InvalidMessage)]
    public void AnEmail_ShouldNeedOnePlainAddress(string? to, string message)
    {
        Assert.Equal(message, Assert.Throws<DomainException>(() => QuoteEmail.Request(Sent(), to, Staff, Today, Now)).Message);
    }

    [Fact]
    public void AnEmail_ShouldNeedWhoAsksForIt()
    {
        Assert.Throws<DomainException>(() => QuoteEmail.Request(Sent(), "valeria@correo.mx", Guid.Empty, Today, Now));
    }

    [Fact]
    public void AnEmail_ShouldEndOnce_SentOrRefused_AndCountTheTriesTheServerPutOff()
    {
        var sent = QuoteEmail.Request(Sent(), "valeria@correo.mx", Staff, Today, Now);
        sent.AttemptFailed(Now.AddSeconds(10));
        sent.AttemptFailed(Now.AddSeconds(40));
        sent.Sent(Now.AddMinutes(3));
        // Hearing it again, or a late failure, changes nothing.
        sent.Fail(QuoteEmailFailure.NotDelivered, Now.AddMinutes(4));
        sent.Sent(Now.AddMinutes(5));
        sent.AttemptFailed(Now.AddMinutes(6));

        Assert.Equal((2, Now.AddSeconds(40), Now.AddMinutes(3), (DateTimeOffset?)null, (QuoteEmailFailure?)null),
            (sent.FailedAttempts, sent.LastFailedAttemptAt, sent.SentAt, sent.FailedAt, sent.Failure));

        var refused = QuoteEmail.Request(Sent(), "valeria@correo.mx", Staff, Today, Now);
        refused.Fail(QuoteEmailFailure.Rejected, Now.AddMinutes(1));
        refused.Sent(Now.AddMinutes(2));
        Assert.Equal((false, (DateTimeOffset?)null, Now.AddMinutes(1), QuoteEmailFailure.Rejected),
            (refused.IsPending, refused.SentAt, refused.FailedAt, refused.Failure));
    }

    [Fact]
    public void AWhatsApp_ShouldCarryTheVersion_ToANumberInE164_AsItsOwnPurpose()
    {
        var quote = Sent();

        var message = OutgoingMessage.ForQuote(quote, "+525522107765", "Valeria Ruiz", "Hola Valeria, tu cotización: /cotizacion#a",
            "cotizacion_lista", "es_MX", ["Valeria Ruiz", "/cotizacion#a"], Staff, Today, Now);

        Assert.Equal((MessagePurpose.QuoteDocument, quote.Id, (Guid?)Staff, Business, "+525522107765"),
            (message.Purpose, message.QuoteId, message.RequestedByUserId, message.TenantId, message.Phone));
        Assert.Equal(("cotizacion_lista", OutgoingMessageStatus.Pending, Now), (message.ProviderTemplateName, message.Status, message.NextAttemptAt));
        Assert.Equal(["Valeria Ruiz", "/cotizacion#a"], message.GetParameters());
        Assert.Null(message.CfdiId);
        Assert.Null(message.LinkToken);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("5522107765")]
    [InlineData("+52 55 2210 7765")]
    public void AWhatsApp_ShouldNeedTheNumberNormalized(string? phone)
    {
        Assert.Equal(InvoiceWhatsApp.InvalidMessage, Assert.Throws<DomainException>(
            () => OutgoingMessage.ForQuote(Sent(), phone, "Valeria", "Hola", null, "es_MX", [], Staff, Today, Now)).Message);
    }

    [Fact]
    public void AWhatsApp_ShouldNeedWhoAsksForIt()
    {
        Assert.Throws<DomainException>(
            () => OutgoingMessage.ForQuote(Sent(), "+525522107765", "Valeria", "Hola", null, "es_MX", [], Guid.Empty, Today, Now));
    }

    [Fact]
    public void ATemplateForQuotes_ShouldTellTheFolio_WhatToPay_UntilWhen_AndCarryTheLink()
    {
        var template = new MessageTemplate(Business, "Cotización", MessagePurpose.QuoteDocument,
            "Hola {nombre}, {negocio} te manda la cotización {folio}: {monto}, vale hasta el {fecha}. Véla aquí: {enlace}", "cotizacion_lista");

        Assert.Equal(MessagePurpose.QuoteDocument, template.Purpose);
        Assert.Equal(["nombre", "negocio", "folio", "monto", "fecha", "enlace"], MessageTemplateText.AllowedPlaceholders(MessagePurpose.QuoteDocument));
        Assert.Equal(MessageTemplateText.MissingQuoteLinkMessage, Assert.Throws<DomainException>(
            () => new MessageTemplate(Business, "Sin enlace", MessagePurpose.QuoteDocument, "Hola {nombre}, tu cotización {folio}")).Message);
        // What a CFDI's template says has no place in a quote's.
        Assert.Contains("{comprobante}", Assert.Throws<DomainException>(
            () => new MessageTemplate(Business, "Otra", MessagePurpose.QuoteDocument, "Tu {comprobante}: {enlace}")).Message);
        // And the folio of a quote has none in a campaign's.
        Assert.Contains("{folio}", Assert.Throws<DomainException>(
            () => new MessageTemplate(Business, "Promo", MessagePurpose.Campaign, "Tu cotización {folio}")).Message);
    }

    [Fact]
    public void TheBusiness_ShouldPickATemplateForItsQuotes_OrTienditAppsText()
    {
        var settings = new TenantSettings(Business, TenantCapability.Service | TenantCapability.Appointments);
        var template = Guid.NewGuid();

        Assert.Null(settings.QuoteWhatsAppTemplateId);
        settings.UseQuoteWhatsAppTemplate(template);
        Assert.Equal(template, settings.QuoteWhatsAppTemplateId);
        settings.UseQuoteWhatsAppTemplate(null);
        Assert.Null(settings.QuoteWhatsAppTemplateId);
        Assert.Throws<DomainException>(() => settings.UseQuoteWhatsAppTemplate(Guid.Empty));
    }
}
