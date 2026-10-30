using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Domain.Messaging;
using FoodParkDigital.Domain.Messaging.Entities;
using FoodParkDigital.Domain.Messaging.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// The WhatsApp message of a stamped CFDI of an order's invoice to its customer (Fase 28M, ADR 0058): its PDF and the link
/// to its XML, through the queue, to one number in E.164. And the number the invoice keeps, the template the business
/// chooses and the template's placeholders.
/// </summary>
public class CfdiWhatsAppTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.FromHours(-6));
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OrderId = Guid.NewGuid();
    private static readonly Guid CashierId = Guid.NewGuid();
    private const string Link = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCdE";

    [Fact]
    public void ForCfdi_ShouldQueueThePdfOfTheStampedInvoice_WithItsTextAndLink()
    {
        var invoice = StampedInvoice();

        var message = OutgoingMessage.ForCfdi(
            invoice, "+525512345678", "UNIVERSIDAD ROBOTICA ESPAÑOLA", "Hola: te enviamos la factura F-7.", "factura_pdf", "es_MX",
            ["UNIVERSIDAD ROBOTICA ESPAÑOLA", "la factura F-7"], Link, null, Now);

        Assert.Equal(
            (TenantId, MessagePurpose.CfdiDocument, (CfdiEmailDocument?)CfdiEmailDocument.Invoice, (Guid?)invoice.Id, (Guid?)invoice.Id,
                (Guid?)null, Link, "+525512345678"),
            (message.TenantId, message.Purpose, message.CfdiDocument, message.CfdiId, message.OrderInvoiceId, message.RequestedByUserId,
                message.LinkToken, message.Phone));
        Assert.Equal(("factura_pdf", "es_MX", OutgoingMessageStatus.Pending, Now), (message.ProviderTemplateName, message.LanguageCode,
            message.Status, message.NextAttemptAt));
        Assert.Equal(["UNIVERSIDAD ROBOTICA ESPAÑOLA", "la factura F-7"], message.GetParameters());
        // Nothing else of the queue: no campaign, appointment nor key against duplicates (it can be sent again).
        Assert.Equal(((Guid?)null, (Guid?)null, (string?)null), (message.CampaignId, message.AppointmentId, message.DeduplicationKey));
    }

    [Fact]
    public void ForCfdi_ShouldTakeTheComplementsAndCreditNotesOfAnInvoice_ToThatInvoice()
    {
        var invoice = StampedInvoice();
        var complement = PaymentComplement.Request(invoice, Payment(50m), "P", 1, 1, 116m, "{}", null, Now);
        complement.Stamp("7a1c2e3f-4b5d-4c6e-8f9a-0b1c2d3e4f5a", "pac-p1", Now, "p-1.xml");
        var note = CreditNote.ForRefund(invoice, Refund(10m), "NC", 1, 8.62m, 1.38m, 1, "{}", null, Now);
        note.Stamp("8b2d3f4a-5c6e-4d7f-9a0b-1c2d3e4f5a6b", "pac-nc1", Now, "nc-1.xml");

        var ofComplement = Message(complement, CashierId);
        var ofNote = Message(note, null);

        Assert.Equal(((Guid?)invoice.Id, (CfdiEmailDocument?)CfdiEmailDocument.PaymentComplement, (Guid?)complement.Id, (Guid?)CashierId),
            (ofComplement.OrderInvoiceId, ofComplement.CfdiDocument, ofComplement.CfdiId, ofComplement.RequestedByUserId));
        Assert.Equal(((Guid?)invoice.Id, (CfdiEmailDocument?)CfdiEmailDocument.CreditNote, (Guid?)note.Id),
            (ofNote.OrderInvoiceId, ofNote.CfdiDocument, ofNote.CfdiId));
    }

    [Fact]
    public void ForCfdi_ShouldOnlyTakeAStampedCfdi_OfAnOrdersInvoice_ToANumber_WithItsLink()
    {
        Assert.Equal(OutgoingMessage.CfdiNotStampedMessage, Assert.Throws<DomainException>(() => Message(Invoice(), null)).Message);

        var cancelling = StampedInvoice();
        cancelling.RequestCancellation(CfdiCancellationMotive.ErrorsWithoutReplacement, CashierId, Now);
        Assert.Throws<DomainException>(() => Message(cancelling, null));

        // A global invoice has no customer to go to.
        var global = GlobalInvoice.Request(TenantId, GlobalInvoicePeriod.Containing(GlobalInvoicePeriodicity.Monthly, new DateOnly(2026, 10, 1)),
            "F", 12, "01", "{}", 100m, 16m, 116m, [new GlobalInvoiceSale(Guid.NewGuid(), "ORD-000001", 116m)], CashierId, Now);
        global.Stamp("9c3e4a5b-6d7f-4e8a-8b1c-2d3e4f5a6b7c", "pac-g", Now, "g.xml");
        Assert.Throws<DomainException>(() => Message(global, null));

        var invoice = StampedInvoice();
        foreach (var phone in new[] { null, "", "5512345678", "+05512345678", "+52 55 1234 5678", "+5255123456789012" })
            Assert.Equal(InvoiceWhatsApp.InvalidMessage, Assert.Throws<DomainException>(() => OutgoingMessage.ForCfdi(
                invoice, phone, null, "Hola", null, "es_MX", [], Link, null, Now)).Message);
        foreach (var link in new[] { "", "corto", Link + "x", Link[..42] + "=" })
            Assert.Throws<DomainException>(() => OutgoingMessage.ForCfdi(invoice, "+525512345678", null, "Hola", null, "es_MX", [], link, null, Now));
        Assert.Throws<DomainException>(() => Message(invoice, Guid.Empty));
    }

    [Fact]
    public void Link_ShouldWorkForNinetyDaysAfterItWasQueued()
    {
        var message = Message(StampedInvoice(), null);
        message.CreatedAt = Now;

        Assert.True(message.LinkWorksAt(Now.AddDays(89)));
        Assert.False(message.LinkWorksAt(Now.AddDays(90)));
        // Any other message has no link.
        var notice = new OutgoingMessage(TenantId, MessagePurpose.AppointmentNotice, "+525512345678", null, "Tu cita", null, "es_MX", [], Now)
        {
            CreatedAt = Now
        };
        Assert.False(notice.LinkWorksAt(Now));
    }

    [Fact]
    public void Invoice_ShouldKeepOneWhatsApp_OrNone()
    {
        var invoice = Invoice();

        invoice.SendWhatsAppTo(" +525512345678 ");
        Assert.Equal("+525512345678", invoice.ReceiverWhatsApp);
        // Mexican mobiles as WhatsApp reports them, and numbers of other countries.
        invoice.SendWhatsAppTo("+5215512345678");
        Assert.Equal("+5215512345678", invoice.ReceiverWhatsApp);
        invoice.SendWhatsAppTo("+14155550123");
        Assert.Equal("+14155550123", invoice.ReceiverWhatsApp);

        Assert.Throws<DomainException>(() => invoice.SendWhatsAppTo("55 1234 5678"));
        Assert.Equal("+14155550123", invoice.ReceiverWhatsApp);
        invoice.SendWhatsAppTo("  ");
        Assert.Null(invoice.ReceiverWhatsApp);
    }

    [Fact]
    public void Profile_ShouldChooseATemplate_OrTienditAppsText()
    {
        var profile = InvoicingProfile.Create(TenantId);
        var templateId = Guid.NewGuid();

        profile.UseWhatsAppTemplate(templateId);
        Assert.Equal(templateId, profile.WhatsAppTemplateId);
        profile.UseWhatsAppTemplate(null);
        Assert.Null(profile.WhatsAppTemplateId);
        Assert.Throws<DomainException>(() => profile.UseWhatsAppTemplate(Guid.Empty));
    }

    [Fact]
    public void Template_ShouldTakeTheCfdisPlaceholders()
    {
        var template = new MessageTemplate(TenantId, "Factura", MessagePurpose.CfdiDocument,
            "Hola {nombre}: {negocio} te envía {comprobante} por {monto}, del {fecha}. XML y PDF: {enlace}", "factura_pdf");

        Assert.Equal(["nombre", "negocio", "comprobante", "monto", "fecha", "enlace"], MessageTemplateText.GetPlaceholders(template.Body));
        var unknown = Assert.Throws<DomainException>(() =>
            new MessageTemplate(TenantId, "Factura", MessagePurpose.CfdiDocument, "Hola {nombre}, tu cita es a las {hora}"));
        Assert.Contains("{hora}", unknown.Message);
        Assert.Contains("{enlace}", unknown.Message);
        // A campaign does not get a CFDI's link.
        Assert.Throws<DomainException>(() => new MessageTemplate(TenantId, "Promo", MessagePurpose.Campaign, "Mira {enlace}"));
        // Without it, the customer would get the PDF alone: the XML is only downloaded with the link.
        Assert.Equal(MessageTemplateText.MissingLinkMessage, Assert.Throws<DomainException>(() =>
            new MessageTemplate(TenantId, "Factura", MessagePurpose.CfdiDocument, "Hola {nombre}: {negocio} te envía {comprobante}")).Message);
        Assert.Throws<DomainException>(() => template.Update("Factura", "Hola {nombre}: tu factura", "factura_pdf", "es_MX"));
    }

    [Fact]
    public void Postpone_ShouldWaitForTheNextTry_WithoutCountingOne()
    {
        var message = Message(StampedInvoice(), null);

        message.Postpone(Now.AddSeconds(20));

        Assert.Equal((OutgoingMessageStatus.Pending, 0, (string?)null, Now.AddSeconds(20), (DateTimeOffset?)null),
            (message.Status, message.Attempts, message.LastError, message.NextAttemptAt, message.LockedUntil));
    }

    private static OutgoingMessage Message(Cfdi cfdi, Guid? requestedBy)
        => OutgoingMessage.ForCfdi(cfdi, "+525512345678", null, "Hola", null, "es_MX", [], Link, requestedBy, Now);

    private static CfdiReceiver Receiver() => CfdiReceiver.Create("URE180429TM6", "UNIVERSIDAD ROBOTICA ESPAÑOLA", "86991", "601", "G03");

    private static OrderPayment Payment(decimal amount) => new(TenantId, OrderId, amount, OrderPaymentMethod.Cash, Now.AddDays(-2), null, null, null, null);

    private static OrderRefund Refund(decimal amount)
        => new(TenantId, OrderId, amount, OrderPaymentMethod.Cash, "Devolución de un producto", Now.AddDays(-1), "cajero", null, null, false);

    private static OrderInvoice Invoice()
        => OrderInvoice.Request(TenantId, OrderId, "F", 7, Receiver(), "99", "{\"tipo\":\"I\"}", 100m, 16m, 116m, CashierId, Now.AddDays(-3), "PPD");

    private static OrderInvoice StampedInvoice()
    {
        var invoice = Invoice();
        invoice.Stamp("5f0b7c1e-2a3d-4e5f-8a9b-0c1d2e3f4a5b", "pac-7", Now.AddDays(-3), "f-7.xml");
        return invoice;
    }
}
