using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// The payment complement (CFDI tipo P, Fase 28C, ADR 0047) of a payment of an order whose invoice is in installments: only
/// for a stamped PPD invoice and a payment of its order, with a form of the SAT's, its installment and the balance before
/// and after it; stamped, refused and cancelled like any CFDI, never deleted.
/// </summary>
public class PaymentComplementTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.FromHours(-6));
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OrderId = Guid.NewGuid();
    private static readonly Guid CashierId = Guid.NewGuid();
    private const string InvoiceUuid = "5f0b7c1e-2a3d-4e5f-8a9b-0c1d2e3f4a5b";
    private const string Uuid = "7a1c2e3f-4b5d-4c6e-8f9a-0b1c2d3e4f5a";
    private const string Document = "{\"tipo\":\"P\"}";

    [Fact]
    public void Request_ShouldFixTheInstallmentAndBalances_OfAPaymentOfAStampedInvoiceInInstallments()
    {
        var invoice = StampedInvoice();
        var payment = Payment(50m, OrderPaymentMethod.Transfer);

        var complement = PaymentComplement.Request(invoice, payment, " p ", 3, 2, 80m, Document, null, Now);

        Assert.Equal((TenantId, invoice.Id, OrderId, payment.Id, payment.PaidAt), (complement.TenantId, complement.OrderInvoiceId,
            complement.OrderId, complement.OrderPaymentId, complement.PaidAt));
        Assert.Equal(("P", 3L, "P-3", "03", 50m, 2, 80m, 30m), (complement.Serie, complement.Folio, complement.Number,
            complement.PaymentForm, complement.Amount, complement.Installment, complement.PreviousBalance, complement.RemainingBalance));
        Assert.Equal((OrderInvoiceStatus.Stamping, Now, Now, Document), (complement.Status, complement.IssuedAt, complement.RequestedAt,
            complement.Document));
        // Issued by TienditApp on its own, or by whoever asked.
        Assert.Null(complement.RequestedByUserId);
        Assert.Equal(CashierId, PaymentComplement.Request(invoice, payment, "P", 4, 2, 80m, Document, CashierId, Now).RequestedByUserId);
        Assert.NotEqual(Guid.Empty, complement.IdempotencyKey);
        Assert.True(complement.IsLive);
    }

    [Fact]
    public void Request_ShouldLeaveNothingOwed_WhenThePaymentCoversTheRest()
    {
        var complement = PaymentComplement.Request(StampedInvoice(), Payment(80m), "P", 1, 1, 80m, Document, null, Now);

        Assert.Equal(0m, complement.RemainingBalance);
    }

    [Fact]
    public void Request_ShouldOnlyGoForAStampedInvoiceInInstallments()
    {
        var payment = Payment(50m);

        // Paid in one exhibition, the invoice already says how it was paid.
        var paid = Assert.Throws<DomainException>(() =>
            PaymentComplement.Request(StampedInvoice(method: "PUE", form: "01"), payment, "P", 1, 1, 116m, Document, null, Now));
        Assert.Contains("PPD", paid.Message);
        // Still stamping, refused or cancelled, the complement has no UUID to point to.
        var stamping = Invoice();
        Assert.Equal(PaymentComplement.NeedsStampedInvoiceMessage, Assert.Throws<DomainException>(() =>
            PaymentComplement.Request(stamping, payment, "P", 1, 1, 116m, Document, null, Now)).Message);
        var cancelled = StampedInvoice();
        cancelled.RequestCancellation(CfdiCancellationMotive.ErrorsWithoutReplacement, CashierId, Now);
        cancelled.Cancel(Now);
        Assert.Throws<DomainException>(() => PaymentComplement.Request(cancelled, payment, "P", 1, 1, 116m, Document, null, Now));
    }

    [Fact]
    public void Request_ShouldOnlyTakeAPaymentOfTheInvoicesOrder()
    {
        var invoice = StampedInvoice();
        var otherOrder = new OrderPayment(TenantId, Guid.NewGuid(), 50m, OrderPaymentMethod.Cash, Now.AddDays(-1), null, null, null, null);
        var otherBusiness = new OrderPayment(Guid.NewGuid(), OrderId, 50m, OrderPaymentMethod.Cash, Now.AddDays(-1), null, null, null, null);

        Assert.Throws<DomainException>(() => PaymentComplement.Request(invoice, otherOrder, "P", 1, 1, 116m, Document, null, Now));
        Assert.Throws<DomainException>(() => PaymentComplement.Request(invoice, otherBusiness, "P", 1, 1, 116m, Document, null, Now));
    }

    [Theory]
    [InlineData(OrderPaymentMethod.Cash, "01")]
    [InlineData(OrderPaymentMethod.Transfer, "03")]
    [InlineData(OrderPaymentMethod.Card, "04")]
    [InlineData(OrderPaymentMethod.DigitalWallet, "06")]
    [InlineData(OrderPaymentMethod.Online, "31")]
    public void Request_ShouldDeclareTheFormThePaymentWasMadeWith(OrderPaymentMethod method, string form)
    {
        Assert.Equal(form, PaymentComplement.Request(StampedInvoice(), Payment(10m, method), "P", 1, 1, 116m, Document, null, Now).PaymentForm);
        Assert.Equal(form, SatCatalog.PaymentComplementForm(method));
    }

    [Theory]
    [InlineData(OrderPaymentMethod.Other)]
    [InlineData(OrderPaymentMethod.CustomerBalance)]
    public void Request_ShouldRefuseAPaymentWithoutAFormOfTheSat(OrderPaymentMethod method)
    {
        // «Por definir» is not allowed in a payment, and the customer's balance is an advance the accountant handles.
        var error = Assert.Throws<DomainException>(() =>
            PaymentComplement.Request(StampedInvoice(), Payment(10m, method), "P", 1, 1, 116m, Document, null, Now));
        Assert.Equal(PaymentComplement.NoPaymentFormMessage, error.Message);
        Assert.Null(SatCatalog.PaymentComplementForm(method));
    }

    [Theory]
    [InlineData(0, 116)]
    [InlineData(1000, 116)]
    [InlineData(1, 0)]
    [InlineData(1, 116.001)]
    [InlineData(1, 117)]
    [InlineData(1, 40)]
    public void Request_ShouldRefuseAnInstallmentOrBalanceThatIsNotValid(int installment, decimal previousBalance)
    {
        // The payment is $50: it cannot pay more than the $40 still owed, nor can more than the invoice's $116 be owed.
        Assert.Throws<DomainException>(() =>
            PaymentComplement.Request(StampedInvoice(), Payment(50m), "P", 1, installment, previousBalance, Document, null, Now));
    }

    [Fact]
    public void Request_ShouldNeedItsSerieFolioDocumentAndWhoAsks()
    {
        var invoice = StampedInvoice();
        var payment = Payment(50m);

        Assert.Throws<DomainException>(() => PaymentComplement.Request(invoice, payment, "P-1", 1, 1, 116m, Document, null, Now));
        Assert.Throws<DomainException>(() => PaymentComplement.Request(invoice, payment, "P", 0, 1, 116m, Document, null, Now));
        Assert.Throws<DomainException>(() => PaymentComplement.Request(invoice, payment, "P", 1, 1, 116m, " ", null, Now));
        Assert.Throws<DomainException>(() => PaymentComplement.Request(invoice, payment, "P", 1, 1, 116m, Document, Guid.Empty, Now));
    }

    [Fact]
    public void Complement_ShouldBeStampedRefusedAndCancelledLikeAnyCfdi_AndKept()
    {
        var stamped = Complement();
        stamped.Stamp(Uuid, "pac-1", Now, "p-1.xml");
        stamped.Stamp(Uuid, "pac-1", Now, "p-1.xml");
        Assert.Equal((OrderInvoiceStatus.Stamped, Uuid.ToUpperInvariant()), (stamped.Status, stamped.Uuid));

        // The receiver may have to accept its cancellation: it waits, then the SAT cancels it, and it stays.
        stamped.RequestCancellation(CfdiCancellationMotive.NotCarriedOut, CashierId, Now);
        Assert.True(stamped.IsLive);
        stamped.Cancel(Now.AddDays(1));
        Assert.Equal((OrderInvoiceStatus.Cancelled, false), (stamped.Status, stamped.IsLive));

        var refused = Complement();
        refused.Refuse("CRP20206 - La suma de ImpPagado no es igual a Monto.", Now);
        Assert.Equal((OrderInvoiceStatus.Refused, false), (refused.Status, refused.IsLive));
    }

    [Theory]
    [InlineData(CfdiCancellationMotive.ErrorsWithReplacement)]
    [InlineData(CfdiCancellationMotive.NominativeInGlobalInvoice)]
    public void Cancellation_ShouldOfferOnlyErrorsWithoutReplacementOrAPaymentThatDidNotHappen(CfdiCancellationMotive motive)
    {
        var complement = Complement();
        complement.Stamp(Uuid, "pac-1", Now, "p-1.xml");

        Assert.Throws<DomainException>(() => complement.RequestCancellation(motive, CashierId, Now));
        Assert.True(complement.TakesCancellationMotive(CfdiCancellationMotive.ErrorsWithoutReplacement));
        Assert.True(complement.TakesCancellationMotive(CfdiCancellationMotive.NotCarriedOut));
        Assert.Equal(OrderInvoiceStatus.Stamped, complement.Status);
    }

    private static PaymentComplement Complement() => PaymentComplement.Request(StampedInvoice(), Payment(50m), "P", 1, 1, 116m, Document, null, Now);

    private static OrderPayment Payment(decimal amount, OrderPaymentMethod method = OrderPaymentMethod.Cash)
        => new(TenantId, OrderId, amount, method, Now.AddDays(-2), null, null, null, null);

    private static OrderInvoice Invoice(string method = "PPD", string form = "99")
        => OrderInvoice.Request(
            TenantId, OrderId, "F", 7, CfdiReceiver.Create("URE180429TM6", "UNIVERSIDAD ROBOTICA ESPAÑOLA", "86991", "601", "G03"),
            form, "{\"tipo\":\"I\"}", 100m, 16m, 116m, CashierId, Now.AddDays(-3), method);

    private static OrderInvoice StampedInvoice(string method = "PPD", string form = "99")
    {
        var invoice = Invoice(method, form);
        invoice.Stamp(InvoiceUuid, "pac-7", Now.AddDays(-3), "f-7.xml");
        return invoice;
    }
}
