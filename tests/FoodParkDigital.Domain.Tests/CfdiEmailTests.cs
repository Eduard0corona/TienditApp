using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Domain.Fiscal.Events;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// The email of a stamped CFDI of an order's invoice to its customer (Fase 28E, ADR 0049): the invoice, a payment complement
/// or a credit note, to one plain address, asked for by TienditApp or a person; only its outcome changes afterwards. And the
/// address the invoice and the customer keep for it.
/// </summary>
public class CfdiEmailTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.FromHours(-6));
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OrderId = Guid.NewGuid();
    private static readonly Guid CashierId = Guid.NewGuid();

    [Fact]
    public void Request_ShouldCarryTheStampedCfdi_ToItsInvoice_AndGoOutFromTheOutbox()
    {
        var invoice = StampedInvoice();

        var email = CfdiEmail.Request(invoice, " karla@ejemplo.mx ", null, Now);

        Assert.Equal((TenantId, invoice.Id, CfdiEmailDocument.Invoice, invoice.Id, "karla@ejemplo.mx", (Guid?)null, Now),
            (email.TenantId, email.OrderInvoiceId, email.Document, email.CfdiId, email.To, email.RequestedByUserId, email.RequestedAt));
        Assert.True(email.IsPending);
        Assert.Equal(email.Id, Assert.IsType<CfdiEmailRequestedEvent>(Assert.Single(email.DomainEvents)).CfdiEmailId);
    }

    [Fact]
    public void Request_ShouldTakeTheComplementsAndCreditNotesOfAnInvoice_ToThatInvoice()
    {
        var invoice = StampedInvoice();
        var complement = PaymentComplement.Request(invoice, Payment(50m), "P", 1, 1, 116m, "{}", null, Now);
        complement.Stamp("7a1c2e3f-4b5d-4c6e-8f9a-0b1c2d3e4f5a", "pac-p1", Now, "p-1.xml");
        var note = CreditNote.ForRefund(invoice, Refund(10m), "NC", 1, 8.62m, 1.38m, 1, "{}", null, Now);
        note.Stamp("8b2d3f4a-5c6e-4d7f-9a0b-1c2d3e4f5a6b", "pac-nc1", Now, "nc-1.xml");

        var ofComplement = CfdiEmail.Request(complement, "karla@ejemplo.mx", CashierId, Now);
        var ofNote = CfdiEmail.Request(note, "karla@ejemplo.mx", CashierId, Now);

        Assert.Equal((invoice.Id, CfdiEmailDocument.PaymentComplement, complement.Id, (Guid?)CashierId),
            (ofComplement.OrderInvoiceId, ofComplement.Document, ofComplement.CfdiId, ofComplement.RequestedByUserId));
        Assert.Equal((invoice.Id, CfdiEmailDocument.CreditNote, note.Id), (ofNote.OrderInvoiceId, ofNote.Document, ofNote.CfdiId));
    }

    [Fact]
    public void Request_ShouldOnlyTakeAStampedCfdi_OfAnOrdersInvoice_ToOnePlainAddress()
    {
        var stamping = Invoice();
        Assert.Equal(CfdiEmail.NotStampedMessage,
            Assert.Throws<DomainException>(() => CfdiEmail.Request(stamping, "karla@ejemplo.mx", null, Now)).Message);

        var cancelling = StampedInvoice();
        cancelling.RequestCancellation(CfdiCancellationMotive.ErrorsWithoutReplacement, CashierId, Now);
        Assert.Throws<DomainException>(() => CfdiEmail.Request(cancelling, "karla@ejemplo.mx", null, Now));

        // A global invoice has no customer to go to.
        var global = GlobalInvoice.Request(TenantId, GlobalInvoicePeriod.Containing(GlobalInvoicePeriodicity.Monthly, new DateOnly(2026, 10, 1)),
            "F", 12, "01", "{}", 100m, 16m, 116m, [new GlobalInvoiceSale(Guid.NewGuid(), "ORD-000001", 116m)], CashierId, Now);
        global.Stamp("9c3e4a5b-6d7f-4e8a-8b1c-2d3e4f5a6b7c", "pac-g", Now, "g.xml");
        Assert.Throws<DomainException>(() => CfdiEmail.Request(global, "karla@ejemplo.mx", null, Now));

        var invoice = StampedInvoice();
        Assert.Throws<DomainException>(() => CfdiEmail.Request(invoice, "  ", null, Now));
        Assert.Equal(InvoiceEmailAddress.InvalidMessage,
            Assert.Throws<DomainException>(() => CfdiEmail.Request(invoice, "Karla <karla@ejemplo.mx>", null, Now)).Message);
        Assert.Throws<DomainException>(() => CfdiEmail.Request(invoice, "karla@ejemplo.mx", Guid.Empty, Now));
    }

    [Fact]
    public void Outcome_ShouldBeSetOnce_WhileTheTriesTheServerPutOffAddUp()
    {
        var email = CfdiEmail.Request(StampedInvoice(), "karla@ejemplo.mx", null, Now);

        email.AttemptFailed(Now.AddMinutes(1));
        email.AttemptFailed(Now.AddMinutes(3));
        Assert.Equal((2, (DateTimeOffset?)Now.AddMinutes(3), true), (email.FailedAttempts, email.LastFailedAttemptAt, email.IsPending));

        email.Sent(withoutPdf: true, Now.AddMinutes(9));
        // The outbox ran it again: what happened first stands.
        email.Sent(withoutPdf: false, Now.AddMinutes(10));
        email.Fail(CfdiEmailFailure.Rejected, Now.AddMinutes(11));
        email.AttemptFailed(Now.AddMinutes(12));

        Assert.Equal(((DateTimeOffset?)Now.AddMinutes(9), true, (DateTimeOffset?)null, (CfdiEmailFailure?)null, 2),
            (email.SentAt, email.SentWithoutPdf, email.FailedAt, email.Failure, email.FailedAttempts));
        Assert.False(email.IsPending);

        var refused = CfdiEmail.Request(StampedInvoice(), "nadie@ejemplo.mx", null, Now);
        refused.Fail(CfdiEmailFailure.NotConfigured, Now);
        refused.Sent(withoutPdf: false, Now);
        Assert.Equal(((DateTimeOffset?)Now, (CfdiEmailFailure?)CfdiEmailFailure.NotConfigured, (DateTimeOffset?)null),
            (refused.FailedAt, refused.Failure, refused.SentAt));
    }

    [Theory]
    [InlineData("karla@ejemplo.mx")]
    [InlineData("karla.fuente+facturas@sub.ejemplo.com.mx")]
    public void Address_ShouldTakeOnePlainAddress(string email)
    {
        Assert.True(InvoiceEmailAddress.IsValid(email));
        Assert.Equal(email, InvoiceEmailAddress.Normalize($"  {email} "));
    }

    [Theory]
    [InlineData("karla")]
    [InlineData("karla@")]
    [InlineData("@ejemplo.mx")]
    [InlineData("karla@ejemplo")]
    [InlineData("karla@ejemplo.mx.")]
    [InlineData("karla@@ejemplo.mx")]
    [InlineData("karla fuente@ejemplo.mx")]
    [InlineData("Karla <karla@ejemplo.mx>")]
    [InlineData("karla@ejemplo.mx, pepe@ejemplo.mx")]
    [InlineData("karla\t@ejemplo.mx")]
    // The platform's SMTP sends seven-bit addresses: these would never go out.
    [InlineData("ñandú@ejemplo.mx")]
    [InlineData("karla@peña.mx")]
    public void Address_ShouldRefuseWhatIsNotOnePlainAddress(string email)
    {
        Assert.False(InvoiceEmailAddress.IsValid(email));
        Assert.Throws<DomainException>(() => InvoiceEmailAddress.Normalize(email));
    }

    [Fact]
    public void Address_ShouldRefuseOneLongerThanAnSmtpServerTakes()
    {
        var local = new string('k', 64);
        var domain = string.Join('.', Enumerable.Repeat(new string('e', 60), 3)) + ".mx";
        Assert.True(InvoiceEmailAddress.IsValid($"{local}@{domain}"));
        Assert.False(InvoiceEmailAddress.IsValid($"{local}@{new string('e', 60)}.{domain}"));
        Assert.Null(InvoiceEmailAddress.Normalize(null));
        Assert.Null(InvoiceEmailAddress.Normalize(" "));
    }

    [Fact]
    public void Invoice_ShouldKeepWhereItsCfdiGo_OrNowhere()
    {
        var invoice = StampedInvoice();

        invoice.SendTo(" karla@ejemplo.mx ");
        Assert.Equal("karla@ejemplo.mx", invoice.ReceiverEmail);
        invoice.SendTo(null);
        Assert.Null(invoice.ReceiverEmail);
        Assert.Throws<DomainException>(() => invoice.SendTo("karla"));
    }

    [Fact]
    public void Customer_ShouldKeepTheEmailForTheirInvoices_WithTheirFiscalData()
    {
        var customer = new Customer(TenantId, "Karla", "5512345678", "karla@personal.mx");

        customer.SetFiscalData(Receiver(), " facturas@ejemplo.mx ");
        Assert.Equal("facturas@ejemplo.mx", customer.InvoiceEmail);
        customer.SetFiscalData(Receiver(), null);
        Assert.Null(customer.InvoiceEmail);
        Assert.Throws<DomainException>(() => customer.SetFiscalData(Receiver(), "facturas"));

        // Forgetting the fiscal data forgets the email with it; the general email stays.
        customer.SetFiscalData(Receiver(), "facturas@ejemplo.mx");
        customer.SetFiscalData(null, "facturas@ejemplo.mx");
        Assert.Equal(((CfdiReceiver?)null, (string?)null, "karla@personal.mx"), (customer.FiscalData, customer.InvoiceEmail, customer.Email));
    }

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
