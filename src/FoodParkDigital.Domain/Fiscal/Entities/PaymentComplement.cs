using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Domain.Orders.Entities;

namespace FoodParkDigital.Domain.Fiscal.Entities;

/// <summary>
/// The payment complement (CFDI tipo P with Recepción de pagos 2.0, Fase 28C, ADR 0047) of one payment of an order whose
/// invoice is paid in installments (PPD): it tells the SAT that this payment covered part of that invoice, which
/// installment it was and what was still owed before and after it. A payment has at most one that stands with the SAT;
/// refused or cancelled, it stays, and another one can be issued.
/// </summary>
public class PaymentComplement : Cfdi, IAggregateRoot
{
    /// <summary>The serie of the complements unless the business chooses another one.</summary>
    public const string DefaultSerie = "P";

    /// <summary>NumParcialidad goes up to 999.</summary>
    public const int MaxInstallment = 999;

    private PaymentComplement() { }

    public Guid OrderInvoiceId { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid OrderPaymentId { get; private set; }

    /// <summary>When the payment was received (FechaPago).</summary>
    public DateTimeOffset PaidAt { get; private set; }

    /// <summary>c_FormaPago of the payment (FormaDePagoP): never «Por definir».</summary>
    public string PaymentForm { get; private set; } = null!;

    /// <summary>What the payment paid of the invoice (Monto and ImpPagado), without its tip.</summary>
    public decimal Amount { get; private set; }

    /// <summary>NumParcialidad: the order in which the invoice's complements were issued.</summary>
    public int Installment { get; private set; }

    /// <summary>ImpSaldoAnt: what the invoice still owed before this payment.</summary>
    public decimal PreviousBalance { get; private set; }

    /// <summary>ImpSaldoInsoluto: what the invoice still owes after it.</summary>
    public decimal RemainingBalance { get; private set; }

    /// <summary>Who asked for it; none when TienditApp issued it on its own, once the payment was registered.</summary>
    public Guid? RequestedByUserId { get; private set; }

    /// <summary>
    /// The complement of a payment of an order whose invoice is in installments and stamped, with its folio, its installment,
    /// the balance before it and its CFDI, before the PAC is asked: what is saved here is what every attempt sends.
    /// </summary>
    public static PaymentComplement Request(
        OrderInvoice invoice,
        OrderPayment payment,
        string serie,
        long folio,
        int installment,
        decimal previousBalance,
        string document,
        Guid? requestedByUserId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(payment);
        if (!invoice.IsPaidInInstallments)
            throw new DomainException("Solo una factura en parcialidades (PPD) lleva complementos de pago.");
        if (invoice.Status != OrderInvoiceStatus.Stamped || invoice.Uuid is null)
            throw new DomainException(NeedsStampedInvoiceMessage);
        if (payment.TenantId != invoice.TenantId || payment.OrderId != invoice.OrderId)
            throw new DomainException("El cobro no es del pedido de la factura.");
        var paymentForm = SatCatalog.PaymentComplementForm(payment.Method)
            ?? throw new DomainException(NoPaymentFormMessage);
        if (installment is < 1 or > MaxInstallment)
            throw new DomainException($"El número de parcialidad va de 1 a {MaxInstallment}.");
        if (previousBalance <= 0 || !IsCents(previousBalance) || previousBalance > invoice.Total)
            throw new DomainException("El saldo anterior de la factura no es válido.");
        if (!IsCents(payment.Amount) || payment.Amount > previousBalance)
            throw new DomainException("El cobro es mayor que lo que la factura todavía debe.");
        if (requestedByUserId == Guid.Empty)
            throw new DomainException("El complemento necesita quién lo pide.");

        var complement = new PaymentComplement
        {
            OrderInvoiceId = invoice.Id,
            OrderId = invoice.OrderId,
            OrderPaymentId = payment.Id,
            PaidAt = payment.PaidAt,
            PaymentForm = paymentForm,
            Amount = payment.Amount,
            Installment = installment,
            PreviousBalance = previousBalance,
            RemainingBalance = previousBalance - payment.Amount,
            RequestedByUserId = requestedByUserId
        };
        complement.Begin(invoice.TenantId, serie, folio, document, now);
        return complement;
    }

    public const string NeedsStampedInvoiceMessage = "La factura necesita estar timbrada para emitir los complementos de sus cobros.";

    /// <summary>«Otro» is none of the SAT's forms, and the customer's balance is an advance the accountant handles.</summary>
    public const string NoPaymentFormMessage =
        "Este cobro no tiene una forma de pago del SAT para su complemento: se cobró con «Otro» o con saldo a favor del cliente.";

    /// <summary>Because it has errors and nothing replaces it (02), or because the payment did not happen (03).</summary>
    public override bool TakesCancellationMotive(CfdiCancellationMotive motive)
        => motive is CfdiCancellationMotive.ErrorsWithoutReplacement or CfdiCancellationMotive.NotCarriedOut;

    protected override string CancellationMotivesMessage
        => "Cancela el complemento por errores sin relación (02) o porque el pago no se llevó a cabo (03).";
}
