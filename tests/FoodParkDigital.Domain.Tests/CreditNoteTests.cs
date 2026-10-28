using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// The credit note (CFDI de egreso, Fase 28D, ADR 0048) of a refund or a write-off of an order whose invoice is stamped: for
/// exactly what was refunded or forgiven, settled with the refund's form or by «Condonación» (15), with amounts that add up;
/// stamped, refused and cancelled like any CFDI, never deleted. A refund of a sale a stamped global invoice covers credits the
/// global invoice instead (Fase 28I, ADR 0054), never more than the sale charged in it.
/// </summary>
public class CreditNoteTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.FromHours(-6));
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OrderId = Guid.NewGuid();
    private static readonly Guid CashierId = Guid.NewGuid();
    private const string InvoiceUuid = "5f0b7c1e-2a3d-4e5f-8a9b-0c1d2e3f4a5b";
    private const string Uuid = "7a1c2e3f-4b5d-4c6e-8f9a-0b1c2d3e4f5a";
    private const string Document = "{\"tipo\":\"E\"}";

    [Fact]
    public void ForRefund_ShouldCreditWhatWasGivenBack_WithTheRefundsForm()
    {
        var invoice = StampedInvoice("PUE", "04");
        var refund = Refund(58m, OrderPaymentMethod.Transfer);

        var note = CreditNote.ForRefund(invoice, refund, " nc ", 3, 50m, 8m, 0, Document, CashierId, Now);

        Assert.Equal((TenantId, invoice.Id, OrderId, refund.Id, (Guid?)null), (note.TenantId, note.OrderInvoiceId, note.OrderId,
            note.OrderRefundId, note.OrderWriteOffId));
        Assert.Equal(("NC", 3L, "NC-3", "03", 50m, 8m, 58m, 0), (note.Serie, note.Folio, note.Number, note.PaymentForm, note.SubTotal,
            note.TaxTotal, note.Total, note.AfterInstallment));
        Assert.Equal((OrderInvoiceStatus.Stamping, Now, Document, CashierId), (note.Status, note.IssuedAt, note.Document,
            note.RequestedByUserId));
        Assert.True(note.IsForRefund);
        Assert.True(note.IsLive);
    }

    [Fact]
    public void ForWriteOff_ShouldCreditWhatWasForgiven_ByCondonacion_AfterTheInstallmentsIssued()
    {
        var invoice = StampedInvoice();
        var writeOff = WriteOff(29m);

        var note = CreditNote.ForWriteOff(invoice, writeOff, "NC", 1, 25m, 4m, 2, Document, null, Now);

        Assert.Equal((writeOff.Id, (Guid?)null, "15", 29m, 2), (note.OrderWriteOffId, note.OrderRefundId, note.PaymentForm, note.Total,
            note.AfterInstallment));
        // TienditApp issued it on its own.
        Assert.Null(note.RequestedByUserId);
        Assert.False(note.IsForRefund);
    }

    [Fact]
    public void CreditNote_ShouldOnlyGoForAStampedInvoice_OfTheSameOrder()
    {
        var refund = Refund(10m);

        var stamping = Assert.Throws<DomainException>(() =>
            CreditNote.ForRefund(Invoice(), refund, "NC", 1, 8.62m, 1.38m, 0, Document, null, Now));
        Assert.Equal(CreditNote.NeedsStampedInvoiceMessage, stamping.Message);

        var otherOrder = new OrderRefund(TenantId, Guid.NewGuid(), 10m, OrderPaymentMethod.Cash, "Devolución", Now, "cajero", null, null, false);
        Assert.Throws<DomainException>(() => CreditNote.ForRefund(StampedInvoice(), otherOrder, "NC", 1, 8.62m, 1.38m, 0, Document, null, Now));
        var otherBusiness = new OrderWriteOff(Guid.NewGuid(), OrderId, 10m, OrderWriteOffReason.Courtesy, null, Now, "dueña");
        Assert.Throws<DomainException>(() => CreditNote.ForWriteOff(StampedInvoice(), otherBusiness, "NC", 1, 8.62m, 1.38m, 0, Document, null, Now));
    }

    [Theory]
    [InlineData(OrderPaymentMethod.Other)]
    [InlineData(OrderPaymentMethod.CustomerBalance)]
    public void ForRefund_ShouldRefuseARefundWithoutAFormOfTheSat(OrderPaymentMethod method)
    {
        var error = Assert.Throws<DomainException>(() =>
            CreditNote.ForRefund(StampedInvoice(), Refund(10m, method), "NC", 1, 8.62m, 1.38m, 0, Document, null, Now));

        Assert.Equal(CreditNote.NoRefundFormMessage, error.Message);
    }

    [Fact]
    public void ForWriteOff_ShouldRefuseOneThatWasUndone()
    {
        var writeOff = WriteOff(10m);
        writeOff.Reverse(Now, "dueña");

        var error = Assert.Throws<DomainException>(() =>
            CreditNote.ForWriteOff(StampedInvoice(), writeOff, "NC", 1, 8.62m, 1.38m, 0, Document, null, Now));

        Assert.Equal(CreditNote.ReversedWriteOffMessage, error.Message);
    }

    [Theory]
    [InlineData(8.62, 1.37)] // they do not add up to what was refunded
    [InlineData(-1, 11)]
    [InlineData(8.625, 1.375)]
    public void CreditNote_ShouldRefuseAmountsThatDoNotAddUp(decimal subTotal, decimal taxTotal)
    {
        Assert.Throws<DomainException>(() =>
            CreditNote.ForRefund(StampedInvoice(), Refund(10m), "NC", 1, subTotal, taxTotal, 0, Document, null, Now));
    }

    [Fact]
    public void CreditNote_ShouldNotCreditMoreThanTheInvoice_NorComeAfterAnInstallmentThatCannotBe()
    {
        // The invoice is for $116.
        Assert.Throws<DomainException>(() =>
            CreditNote.ForRefund(StampedInvoice(), Refund(117m), "NC", 1, 100.86m, 16.14m, 0, Document, null, Now));
        Assert.Throws<DomainException>(() =>
            CreditNote.ForRefund(StampedInvoice(), Refund(10m), "NC", 1, 8.62m, 1.38m, -1, Document, null, Now));
        Assert.Throws<DomainException>(() =>
            CreditNote.ForRefund(StampedInvoice(), Refund(10m), "NC", 1, 8.62m, 1.38m, 1000, Document, null, Now));
    }

    [Fact]
    public void CreditNote_ShouldNeedItsSerieFolioDocumentAndWhoAsks()
    {
        var invoice = StampedInvoice();
        var refund = Refund(10m);

        Assert.Throws<DomainException>(() => CreditNote.ForRefund(invoice, refund, "N-C", 1, 8.62m, 1.38m, 0, Document, null, Now));
        Assert.Throws<DomainException>(() => CreditNote.ForRefund(invoice, refund, "NC", 0, 8.62m, 1.38m, 0, Document, null, Now));
        Assert.Throws<DomainException>(() => CreditNote.ForRefund(invoice, refund, "NC", 1, 8.62m, 1.38m, 0, " ", null, Now));
        Assert.Throws<DomainException>(() => CreditNote.ForRefund(invoice, refund, "NC", 1, 8.62m, 1.38m, 0, Document, Guid.Empty, Now));
    }

    [Fact]
    public void CreditNote_ShouldBeStampedRefusedAndCancelledLikeAnyCfdi_AndKept()
    {
        var stamped = Note();
        stamped.Stamp(Uuid, "pac-1", Now, "nc-1.xml");
        Assert.Equal((OrderInvoiceStatus.Stamped, Uuid.ToUpperInvariant()), (stamped.Status, stamped.Uuid));

        stamped.RequestCancellation(CfdiCancellationMotive.ErrorsWithoutReplacement, CashierId, Now);
        Assert.True(stamped.IsLive);
        stamped.Cancel(Now.AddDays(1));
        Assert.Equal((OrderInvoiceStatus.Cancelled, false), (stamped.Status, stamped.IsLive));

        var refused = Note();
        refused.Refuse("CFDI40108 - El TipoDeComprobante es E y la FormaPago no es válida.", Now);
        Assert.Equal((OrderInvoiceStatus.Refused, false), (refused.Status, refused.IsLive));
    }

    [Theory]
    [InlineData(CfdiCancellationMotive.ErrorsWithReplacement)]
    [InlineData(CfdiCancellationMotive.NominativeInGlobalInvoice)]
    [InlineData(CfdiCancellationMotive.NotCarriedOut)]
    public void Cancellation_OfARefundsNote_ShouldOfferOnlyErrorsWithoutReplacement(CfdiCancellationMotive motive)
    {
        // A refund recorded here is never undone: «not carried out» does not fit its credit note.
        var note = Note();
        note.Stamp(Uuid, "pac-1", Now, "nc-1.xml");

        var refused = Assert.Throws<DomainException>(() => note.RequestCancellation(motive, CashierId, Now));
        Assert.Equal(CreditNote.RefundCancellationMotivesMessage, refused.Message);
        Assert.True(note.TakesCancellationMotive(CfdiCancellationMotive.ErrorsWithoutReplacement));
        Assert.Equal(OrderInvoiceStatus.Stamped, note.Status);
    }

    [Fact]
    public void Cancellation_OfAWriteOffsNote_ShouldAlsoTakeAWriteOffThatDidNotHappen()
    {
        var note = CreditNote.ForWriteOff(StampedInvoice(), WriteOff(10m), "NC", 1, 8.62m, 1.38m, 0, Document, null, Now);
        note.Stamp(Uuid, "pac-1", Now, "nc-1.xml");

        Assert.True(note.TakesCancellationMotive(CfdiCancellationMotive.ErrorsWithoutReplacement));
        Assert.False(note.TakesCancellationMotive(CfdiCancellationMotive.ErrorsWithReplacement));
        note.RequestCancellation(CfdiCancellationMotive.NotCarriedOut, CashierId, Now);
        Assert.Equal((OrderInvoiceStatus.Cancelling, CfdiCancellationMotive.NotCarriedOut), (note.Status, note.CancellationMotive));
    }

    [Fact]
    public void ForRefundInGlobalInvoice_ShouldCreditTheSaleInTheGlobalInvoice_WithTheRefundsForm()
    {
        var (global, sale) = StampedGlobal();
        var refund = Refund(58m, OrderPaymentMethod.Card);

        var note = CreditNote.ForRefundInGlobalInvoice(global, sale, refund, "NC", 4, 50m, 8m, Document, null, Now);

        Assert.Equal((TenantId, (Guid?)null, (Guid?)global.Id, OrderId, (Guid?)refund.Id, (Guid?)null),
            (note.TenantId, note.OrderInvoiceId, note.GlobalInvoiceId, note.OrderId, note.OrderRefundId, note.OrderWriteOffId));
        // Paid in one exhibition: no installment before it.
        Assert.Equal(("NC-4", "04", 50m, 8m, 58m, 0, OrderInvoiceStatus.Stamping), (note.Number, note.PaymentForm, note.SubTotal,
            note.TaxTotal, note.Total, note.AfterInstallment, note.Status));
        Assert.True(note.IsForGlobalInvoice);
        Assert.True(note.IsForRefund);
        // A refund recorded here is never undone: only errors (02) cancel it.
        Assert.False(note.TakesCancellationMotive(CfdiCancellationMotive.NotCarriedOut));
        Assert.False(Note().IsForGlobalInvoice);
    }

    [Fact]
    public void ForRefundInGlobalInvoice_ShouldOnlyGoForAStampedGlobalInvoice_ThatCoversTheSale_UpToWhatTheSaleCharged()
    {
        var (global, sale) = StampedGlobal();

        // The sale charged $116 in the global invoice.
        var tooMuch = Assert.Throws<DomainException>(() => CreditNote.ForRefundInGlobalInvoice(
            global, sale, Refund(116.01m), "NC", 1, 100.01m, 16m, Document, null, Now));
        Assert.Equal("La nota de crédito es mayor que lo que la venta cobró en la factura global.", tooMuch.Message);
        Assert.Equal(116m, CreditNote.ForRefundInGlobalInvoice(global, sale, Refund(116m), "NC", 1, 100m, 16m, Document, null, Now).Total);

        var stamping = Global();
        var notStamped = Assert.Throws<DomainException>(() => CreditNote.ForRefundInGlobalInvoice(
            stamping, stamping.Orders.First(o => o.OrderId == OrderId), Refund(10m), "NC", 1, 8.62m, 1.38m, Document, null, Now));
        Assert.Equal(CreditNote.NeedsStampedGlobalInvoiceMessage, notStamped.Message);

        // A sale of another global invoice, or a refund of another sale or business.
        var (otherGlobal, otherSale) = StampedGlobal();
        Assert.Throws<DomainException>(() => CreditNote.ForRefundInGlobalInvoice(global, otherSale, Refund(10m), "NC", 1, 8.62m, 1.38m, Document, null, Now));
        var otherOrder = new OrderRefund(TenantId, Guid.NewGuid(), 10m, OrderPaymentMethod.Cash, "Devolución", Now, "cajero", null, null, false);
        Assert.Throws<DomainException>(() => CreditNote.ForRefundInGlobalInvoice(global, sale, otherOrder, "NC", 1, 8.62m, 1.38m, Document, null, Now));
        var otherBusiness = new OrderRefund(Guid.NewGuid(), OrderId, 10m, OrderPaymentMethod.Cash, "Devolución", Now, "cajero", null, null, false);
        Assert.Throws<DomainException>(() => CreditNote.ForRefundInGlobalInvoice(global, sale, otherBusiness, "NC", 1, 8.62m, 1.38m, Document, null, Now));
        Assert.NotNull(otherGlobal);

        // Nor one without a form of the SAT's, nor with amounts that do not add up.
        var noForm = Assert.Throws<DomainException>(() => CreditNote.ForRefundInGlobalInvoice(
            global, sale, Refund(10m, OrderPaymentMethod.Other), "NC", 1, 8.62m, 1.38m, Document, null, Now));
        Assert.Equal(CreditNote.NoRefundFormMessage, noForm.Message);
        Assert.Throws<DomainException>(() => CreditNote.ForRefundInGlobalInvoice(global, sale, Refund(10m), "NC", 1, 8.62m, 1.37m, Document, null, Now));
    }

    [Fact]
    public void ForRefundInGlobalInvoice_ShouldNotGoForASaleTheGlobalInvoiceReleased()
    {
        // Cancelled, the global invoice frees its sales: they no longer take its credit notes.
        var (global, sale) = StampedGlobal();
        global.RequestCancellation(CfdiCancellationMotive.ErrorsWithoutReplacement, CashierId, Now);
        global.Cancel(Now);

        var error = Assert.Throws<DomainException>(() =>
            CreditNote.ForRefundInGlobalInvoice(global, sale, Refund(10m), "NC", 1, 8.62m, 1.38m, Document, null, Now));
        Assert.Equal("La venta no está en esa factura global.", error.Message);
    }

    [Theory]
    [InlineData("601", "G02")]
    [InlineData("612", "G02")]
    [InlineData("605", "S01")] // wages: no purchases, so no returns either
    [InlineData("616", "S01")]
    public void CreditNoteUse_ShouldBeReturnsAndDiscounts_UnlessTheReceiversRegimeCannotAskForIt(string regime, string use)
    {
        Assert.Equal(use, SatCatalog.CreditNoteUseFor(regime));
    }

    private static CreditNote Note() => CreditNote.ForRefund(StampedInvoice(), Refund(10m), "NC", 1, 8.62m, 1.38m, 0, Document, null, Now);

    private static OrderRefund Refund(decimal amount, OrderPaymentMethod method = OrderPaymentMethod.Cash)
        => new(TenantId, OrderId, amount, method, "Devolución de un producto", Now.AddDays(-1), "cajero", null, null, false);

    private static OrderWriteOff WriteOff(decimal amount)
        => new(TenantId, OrderId, amount, OrderWriteOffReason.WontBeCollected, null, Now.AddDays(-1), "dueña");

    private static OrderInvoice Invoice(string method = "PPD", string form = "99")
        => OrderInvoice.Request(
            TenantId, OrderId, "F", 7, CfdiReceiver.Create("URE180429TM6", "UNIVERSIDAD ROBOTICA ESPAÑOLA", "86991", "601", "G03"),
            form, "{\"tipo\":\"I\"}", 100m, 16m, 116m, CashierId, Now.AddDays(-3), method);

    /// <summary>A stamped global invoice that covers the order (for $116) and another sale.</summary>
    private static (GlobalInvoice Global, GlobalInvoiceOrder Sale) StampedGlobal()
    {
        var global = Global();
        global.Stamp(Guid.NewGuid().ToString(), "pac-g", Now.AddDays(-2), "g.xml");
        return (global, global.Orders.First(o => o.OrderId == OrderId));
    }

    private static GlobalInvoice Global()
        => GlobalInvoice.Request(
            TenantId,
            GlobalInvoicePeriod.Containing(GlobalInvoicePeriodicity.Daily, new DateOnly(2026, 10, 20)),
            "F",
            12,
            "01",
            "{\"tipo\":\"I\"}",
            200m,
            32m,
            232m,
            [new GlobalInvoiceSale(OrderId, "ORD-000001", 116m), new GlobalInvoiceSale(Guid.NewGuid(), "ORD-000002", 116m)],
            CashierId,
            Now.AddDays(-2));

    private static OrderInvoice StampedInvoice(string method = "PPD", string form = "99")
    {
        var invoice = Invoice(method, form);
        invoice.Stamp(InvoiceUuid, "pac-7", Now.AddDays(-3), "f-7.xml");
        return invoice;
    }
}
