using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal.Enums;

namespace FoodParkDigital.Domain.Fiscal.Entities;

/// <summary>
/// The CFDI 4.0 invoice (ingreso) of an order to a customer with an RFC, stamped through the platform's PAC with the
/// business's CSD (Fase 28A, ADR 0045). Its amounts are the ones the backend computed for the order's CFDI draft. A sale not
/// paid in full is invoiced in installments (PPD, Fase 28C, ADR 0047), and each of its payments gets a complement. A
/// cancelled one stays, and the order can be invoiced again.
/// </summary>
public class OrderInvoice : CfdiInvoice, IAggregateRoot
{
    /// <summary>
    /// Who requested it when the customer invoiced their ticket online (Fase 28F, ADR 0050): a value no account has, so who
    /// requested an invoice is always known.
    /// </summary>
    public static readonly Guid SelfInvoiceRequester = new("00000000-0000-0000-0000-00000000028f");

    private OrderInvoice() { }

    public Guid OrderId { get; private set; }

    public CfdiReceiver Receiver { get; private set; } = null!;

    /// <summary>
    /// Where the customer gets its XML and PDF, and those of its payment complements and credit notes, once each is stamped
    /// (Fase 28E, ADR 0049); null when nobody gets them by email. It is not part of the CFDI.
    /// </summary>
    public string? ReceiverEmail { get; private set; }

    /// <summary>
    /// The invoice of an order, with its folio and the amounts of its CFDI draft, before the PAC is asked: what is saved here
    /// is what every attempt sends. Paid in full, in one exhibition (PUE) with the form that paid it; otherwise in
    /// installments (PPD) with «Por definir» (99).
    /// </summary>
    public static OrderInvoice Request(
        Guid tenantId,
        Guid orderId,
        string serie,
        long folio,
        CfdiReceiver receiver,
        string paymentForm,
        string document,
        decimal subTotal,
        decimal taxTotal,
        decimal total,
        Guid requestedByUserId,
        DateTimeOffset now,
        string paymentMethod = PaidInOneExhibition)
    {
        if (tenantId == Guid.Empty || orderId == Guid.Empty || requestedByUserId == Guid.Empty)
            throw new DomainException("La factura necesita su negocio, su pedido y quién la pide.");
        ArgumentNullException.ThrowIfNull(receiver);

        var invoice = new OrderInvoice { OrderId = orderId, Receiver = receiver };
        invoice.Begin(tenantId, serie, folio, paymentForm, paymentMethod, document, subTotal, taxTotal, total, requestedByUserId, now);
        return invoice;
    }

    /// <summary>
    /// The invoice of the same order that replaces this one because this one has errors (Fase 28G, ADR 0051): it carries
    /// this one's UUID with the relation «Sustitución de los CFDI previos» (04), and this one is cancelled with motive 01 and
    /// its UUID once it is stamped. From then on this one is no longer the order's invoice, whatever the SAT says of it.
    /// </summary>
    public Guid? ReplacedByInvoiceId { get; private set; }

    /// <summary>Whether the customer requested it online with their ticket, and not the business.</summary>
    public bool RequestedByCustomer => RequestedByUserId == SelfInvoiceRequester;

    /// <summary>The order's invoice now: one that stands (or may) with the SAT and that no other replaces.</summary>
    public bool IsCurrent => IsLive && ReplacedByInvoiceId is null;

    /// <summary>Where its CFDI go by email from now on, or nowhere (null).</summary>
    public void SendTo(string? email) => ReceiverEmail = InvoiceEmailAddress.Normalize(email);

    /// <summary>
    /// Another invoice of the same order replaces this stamped one, which then waits to be cancelled with motive 01. The
    /// replacement is just being requested: it is stamped after this is saved.
    /// </summary>
    public void ReplaceWith(OrderInvoice replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (replacement.Id == Id || replacement.TenantId != TenantId || replacement.OrderId != OrderId)
            throw new DomainException("La factura que sustituye a otra es del mismo pedido.");
        if (replacement.Status != OrderInvoiceStatus.Stamping)
            throw new DomainException("La factura que sustituye a otra se pide antes de timbrarse.");
        if (ReplacedByInvoiceId is not null)
            throw new DomainException(AlreadyReplacedMessage);
        if (Status != OrderInvoiceStatus.Stamped)
            throw new DomainException(Status == OrderInvoiceStatus.Cancelling
                ? "Esta factura ya espera su cancelación: no se sustituye."
                : "Solo se sustituye una factura timbrada.");

        ReplacedByInvoiceId = replacement.Id;
        FailureMessage = null;
    }

    public const string AlreadyReplacedMessage = "Esta factura ya tiene otra que la sustituye.";

    /// <summary>
    /// The PAC refused the invoice that was to replace this one: this one is the order's invoice again, and can be replaced
    /// by another. Hearing it for a replacement that is not this one's changes nothing.
    /// </summary>
    public void KeepAfterRefusedReplacement(Guid replacementId)
    {
        if (ReplacedByInvoiceId != replacementId)
            return;
        // The replacement never stood, so this one was never asked to be cancelled with motive 01.
        if (Status != OrderInvoiceStatus.Stamped)
            throw new DomainException("Una factura cuya cancelación se pidió sigue sustituida.");

        ReplacedByInvoiceId = null;
    }

    /// <summary>
    /// The PAC or the SAT did not take its cancellation with motive 01 (the SAT may not have its replacement yet): it stays
    /// stamped and replaced, with the reason, to be asked again.
    /// </summary>
    public void KeepAfterFailedReplacementCancellation(string? message)
    {
        if (ReplacedByInvoiceId is null || Status != OrderInvoiceStatus.Stamped)
            throw new DomainException("Solo una factura sustituida y timbrada espera su cancelación con relación (01).");

        FailureMessage = Trim(message, "El SAT no canceló la factura sustituida.");
    }

    /// <summary>
    /// Because it has errors and nothing replaces it (02), or because the sale did not happen (03); because it has errors
    /// and another invoice replaces it (01), only once one does (Fase 28G).
    /// </summary>
    public override bool TakesCancellationMotive(CfdiCancellationMotive motive)
        => motive is CfdiCancellationMotive.ErrorsWithoutReplacement or CfdiCancellationMotive.NotCarriedOut
            || (motive == CfdiCancellationMotive.ErrorsWithReplacement && ReplacedByInvoiceId is not null);

    protected override string CancellationMotivesMessage => ReplacedByInvoiceId is null
        ? "Cancela por errores sin relación (02) o porque la venta no se llevó a cabo (03); para cancelar con relación (01), sustitúyela."
        : "Cancela por errores con relación (01), sin relación (02) o porque la venta no se llevó a cabo (03).";
}
