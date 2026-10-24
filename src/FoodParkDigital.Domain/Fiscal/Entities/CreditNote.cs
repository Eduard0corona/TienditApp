using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Domain.Orders.Entities;

namespace FoodParkDigital.Domain.Fiscal.Entities;

/// <summary>
/// The credit note (CFDI de egreso, tipo E, Fase 28D, ADR 0048) of an adjustment of an order whose invoice is stamped: a
/// refund (money given back) or a write-off (a debt the business forgave). It is related to the invoice (TipoRelacion 01)
/// and lowers what the invoice amounts to by exactly what was refunded or forgiven. An adjustment has at most one credit
/// note that stands with the SAT; refused or cancelled, it stays, and another one can be issued.
/// </summary>
public class CreditNote : Cfdi, IAggregateRoot
{
    /// <summary>The serie of the credit notes unless the business chooses another one.</summary>
    public const string DefaultSerie = "NC";

    private CreditNote() { }

    public Guid OrderInvoiceId { get; private set; }
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

    /// <summary>«Otro» is none of the SAT's forms, and a refund to the customer's balance is an advance the accountant handles.</summary>
    public const string NoRefundFormMessage =
        "Este reembolso no tiene una forma de pago del SAT para su nota de crédito: se devolvió con «Otro» o como saldo a favor del cliente.";

    public const string ReversedWriteOffMessage = "Esa condonación ya se deshizo: no lleva nota de crédito.";

    /// <summary>Because it has errors and nothing replaces it (02), or because the adjustment did not happen (03).</summary>
    public override bool TakesCancellationMotive(CfdiCancellationMotive motive)
        => motive is CfdiCancellationMotive.ErrorsWithoutReplacement or CfdiCancellationMotive.NotCarriedOut;

    protected override string CancellationMotivesMessage
        => "Cancela la nota de crédito por errores sin relación (02) o porque la operación no se llevó a cabo (03).";

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
        if (total <= 0 || !IsCents(total) || total > invoice.Total)
            throw new DomainException("La nota de crédito es mayor que la factura.");
        if (subTotal < 0 || taxTotal < 0 || !IsCents(subTotal) || !IsCents(taxTotal) || subTotal + taxTotal != total)
            throw new DomainException("Los importes de la nota de crédito no cuadran.");
        if (afterInstallment is < 0 or > PaymentComplement.MaxInstallment)
            throw new DomainException("La parcialidad de la nota de crédito no es válida.");
        if (requestedByUserId == Guid.Empty)
            throw new DomainException("La nota de crédito necesita quién la pide.");

        OrderInvoiceId = invoice.Id;
        OrderId = invoice.OrderId;
        SubTotal = subTotal;
        TaxTotal = taxTotal;
        Total = total;
        AfterInstallment = afterInstallment;
        RequestedByUserId = requestedByUserId;
        Begin(invoice.TenantId, serie, folio, document, now);
    }
}
