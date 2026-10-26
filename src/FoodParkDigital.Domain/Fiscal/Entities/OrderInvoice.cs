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

    /// <summary>Whether the customer requested it online with their ticket, and not the business.</summary>
    public bool RequestedByCustomer => RequestedByUserId == SelfInvoiceRequester;

    /// <summary>Where its CFDI go by email from now on, or nowhere (null).</summary>
    public void SendTo(string? email) => ReceiverEmail = InvoiceEmailAddress.Normalize(email);

    /// <summary>Because it has errors and nothing replaces it (02), or because the sale did not happen (03).</summary>
    public override bool TakesCancellationMotive(CfdiCancellationMotive motive)
        => motive is CfdiCancellationMotive.ErrorsWithoutReplacement or CfdiCancellationMotive.NotCarriedOut;

    protected override string CancellationMotivesMessage
        => "Cancela por errores sin relación (02) o porque la venta no se llevó a cabo (03).";
}
