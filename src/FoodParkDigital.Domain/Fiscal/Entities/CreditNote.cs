using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Domain.Orders.Entities;

namespace FoodParkDigital.Domain.Fiscal.Entities;

/// <summary>
/// The credit note (CFDI de egreso, tipo E, Fase 28D, ADR 0048) of an adjustment of an order whose invoice is stamped: a
/// refund (money given back) or a write-off (a debt the business forgave). It is related to the invoice (TipoRelacion 01)
/// and lowers what the invoice amounts to by exactly what was refunded or forgiven. A refund of a sale a stamped global
/// invoice covers is related to the global invoice instead (Fase 28I, ADR 0054), to the general public. An adjustment has
/// at most one credit note that stands with the SAT; refused or cancelled, it stays, and another one can be issued.
/// </summary>
public class CreditNote : Cfdi, IAggregateRoot
{
    /// <summary>The serie of the credit notes unless the business chooses another one.</summary>
    public const string DefaultSerie = "NC";

    private CreditNote() { }

    /// <summary>The order's invoice it credits; null when it credits a global invoice.</summary>
    public Guid? OrderInvoiceId { get; private set; }

    /// <summary>The global invoice it credits, for a sale it covers (Fase 28I); null when it credits an order's invoice.</summary>
    public Guid? GlobalInvoiceId { get; private set; }

    public Guid OrderId { get; private set; }

    /// <summary>The refund it credits; null when it credits a write-off.</summary>
    public Guid? OrderRefundId { get; private set; }

    /// <summary>The write-off it credits; null when it credits a refund.</summary>
    public Guid? OrderWriteOffId { get; private set; }

    /// <summary>c_FormaPago: the refund's form (never «Por definir» nor an advance), or «Condonación» (15) for a write-off.</summary>
    public string PaymentForm { get; private set; } = null!;

    public decimal SubTotal { get; private set; }
    public decimal TaxTotal { get; private set; }

    /// <summary>What it credits of the invoice: exactly what was refunded or forgiven.</summary>
    public decimal Total { get; private set; }

    /// <summary>
    /// The highest installment of the invoice's payment complements when it was asked for (0 without any, or for an invoice
    /// paid in one exhibition): the complements of the installments after it count it in the balance before them.
    /// </summary>
    public int AfterInstallment { get; private set; }

    /// <summary>Who asked for it; none when TienditApp issued it on its own.</summary>
    public Guid? RequestedByUserId { get; private set; }

    /// <summary>Whether it credits a refund (otherwise, a write-off).</summary>
    public bool IsForRefund => OrderRefundId is not null;

    /// <summary>Whether it credits a sale of a global invoice (otherwise, an order's invoice).</summary>
    public bool IsForGlobalInvoice => GlobalInvoiceId is not null;

    /// <summary>The credit note of a refund of the invoice's order, with its folio, amounts and CFDI, before the PAC is asked.</summary>
    public static CreditNote ForRefund(
        OrderInvoice invoice,
        OrderRefund refund,
        string serie,
        long folio,
        decimal subTotal,
        decimal taxTotal,
        int afterInstallment,
        string document,
        Guid? requestedByUserId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(refund);
        if (refund.TenantId != invoice.TenantId || refund.OrderId != invoice.OrderId)
            throw new DomainException("El reembolso no es del pedido de la factura.");
        var paymentForm = SatCatalog.PaymentComplementForm(refund.Method) ?? throw new DomainException(NoRefundFormMessage);

        var note = new CreditNote { OrderRefundId = refund.Id, PaymentForm = paymentForm };
        note.Begin(invoice, serie, folio, refund.Amount, subTotal, taxTotal, afterInstallment, document, requestedByUserId, now);
        return note;
    }

    /// <summary>
    /// The credit note of a refund of a sale a stamped global invoice covers (Fase 28I), with its folio, amounts and CFDI,
    /// before the PAC is asked. It never credits more than what the sale charged in the global invoice; a global invoice is
    /// paid in one exhibition, so no installment comes before it.
    /// </summary>
    public static CreditNote ForRefundInGlobalInvoice(
        GlobalInvoice globalInvoice,
        GlobalInvoiceOrder sale,
        OrderRefund refund,
        string serie,
        long folio,
        decimal subTotal,
        decimal taxTotal,
        string document,
        Guid? requestedByUserId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(globalInvoice);
        ArgumentNullException.ThrowIfNull(sale);
        ArgumentNullException.ThrowIfNull(refund);
        if (sale.TenantId != globalInvoice.TenantId || sale.GlobalInvoiceId != globalInvoice.Id || sale.Released)
            throw new DomainException("La venta no está en esa factura global.");
        if (refund.TenantId != globalInvoice.TenantId || refund.OrderId != sale.OrderId)
            throw new DomainException("El reembolso no es de la venta de la factura global.");
        if (globalInvoice.Status != OrderInvoiceStatus.Stamped || globalInvoice.Uuid is null)
            throw new DomainException(NeedsStampedGlobalInvoiceMessage);
        var paymentForm = SatCatalog.PaymentComplementForm(refund.Method) ?? throw new DomainException(NoRefundFormMessage);
        if (refund.Amount > sale.Total)
            throw new DomainException("La nota de crédito es mayor que lo que la venta cobró en la factura global.");

        var note = new CreditNote
        {
            GlobalInvoiceId = globalInvoice.Id,
            OrderId = sale.OrderId,
            OrderRefundId = refund.Id,
            PaymentForm = paymentForm
        };
        note.Begin(globalInvoice.TenantId, serie, folio, refund.Amount, subTotal, taxTotal, afterInstallment: 0, document,
            requestedByUserId, now);
        return note;
    }

    /// <summary>
    /// The credit note of a write-off of the invoice's order that still stands, with its folio, amounts and CFDI, before the
    /// PAC is asked: paid by «Condonación» (15).
    /// </summary>
    public static CreditNote ForWriteOff(
        OrderInvoice invoice,
        OrderWriteOff writeOff,
        string serie,
        long folio,
        decimal subTotal,
        decimal taxTotal,
        int afterInstallment,
        string document,
        Guid? requestedByUserId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(writeOff);
        if (writeOff.TenantId != invoice.TenantId || writeOff.OrderId != invoice.OrderId)
            throw new DomainException("La condonación no es del pedido de la factura.");
        if (writeOff.IsReversed)
            throw new DomainException(ReversedWriteOffMessage);

        var note = new CreditNote { OrderWriteOffId = writeOff.Id, PaymentForm = SatCatalog.PaymentFormWriteOff };
        note.Begin(invoice, serie, folio, writeOff.Amount, subTotal, taxTotal, afterInstallment, document, requestedByUserId, now);
        return note;
    }

    public const string NeedsStampedInvoiceMessage = "La factura necesita estar timbrada para emitir sus notas de crédito.";

    public const string NeedsStampedGlobalInvoiceMessage =
        "La factura global necesita estar timbrada para emitir las notas de crédito de sus ventas.";

    /// <summary>«Otro» is none of the SAT's forms, and a refund to the customer's balance is an advance the accountant handles.</summary>
    public const string NoRefundFormMessage =
        "Este reembolso no tiene una forma de pago del SAT para su nota de crédito: se devolvió con «Otro» o como saldo a favor del cliente.";

    public const string ReversedWriteOffMessage = "Esa condonación ya se deshizo: no lleva nota de crédito.";

    /// <summary>A refund recorded here is never undone: its credit note is cancelled only because it has errors (02).</summary>
    public const string RefundCancellationMotivesMessage =
        "La nota de crédito de un reembolso se cancela por errores sin relación (02): el reembolso registrado no se deshace.";

    /// <summary>
    /// Because it has errors and nothing replaces it (02); a write-off's also because it did not happen (03), before it is
    /// undone. A refund recorded here is never undone, so its credit note takes only 02.
    /// </summary>
    public override bool TakesCancellationMotive(CfdiCancellationMotive motive)
        => motive == CfdiCancellationMotive.ErrorsWithoutReplacement
            || (!IsForRefund && motive == CfdiCancellationMotive.NotCarriedOut);

    protected override string CancellationMotivesMessage => IsForRefund
        ? RefundCancellationMotivesMessage
        : "Cancela la nota de crédito por errores sin relación (02) o porque la condonación no se llevó a cabo (03).";

    private void Begin(
        OrderInvoice invoice,
        string serie,
        long folio,
        decimal total,
        decimal subTotal,
        decimal taxTotal,
        int afterInstallment,
        string document,
        Guid? requestedByUserId,
        DateTimeOffset now)
    {
        if (invoice.Status != OrderInvoiceStatus.Stamped || invoice.Uuid is null)
            throw new DomainException(NeedsStampedInvoiceMessage);
        if (total > invoice.Total)
            throw new DomainException("La nota de crédito es mayor que la factura.");

        OrderInvoiceId = invoice.Id;
        OrderId = invoice.OrderId;
        Begin(invoice.TenantId, serie, folio, total, subTotal, taxTotal, afterInstallment, document, requestedByUserId, now);
    }

    private void Begin(
        Guid tenantId,
        string serie,
        long folio,
        decimal total,
        decimal subTotal,
        decimal taxTotal,
        int afterInstallment,
        string document,
        Guid? requestedByUserId,
        DateTimeOffset now)
    {
        if (total <= 0 || !IsCents(total))
            throw new DomainException("El importe de la nota de crédito no es válido.");
        if (subTotal < 0 || taxTotal < 0 || !IsCents(subTotal) || !IsCents(taxTotal) || subTotal + taxTotal != total)
            throw new DomainException("Los importes de la nota de crédito no cuadran.");
        if (afterInstallment is < 0 or > PaymentComplement.MaxInstallment)
            throw new DomainException("La parcialidad de la nota de crédito no es válida.");
        if (requestedByUserId == Guid.Empty)
            throw new DomainException("La nota de crédito necesita quién la pide.");

        SubTotal = subTotal;
        TaxTotal = taxTotal;
        Total = total;
        AfterInstallment = afterInstallment;
        RequestedByUserId = requestedByUserId;
        Begin(tenantId, serie, folio, document, now);
    }
}
