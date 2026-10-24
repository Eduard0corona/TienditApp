using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal.Enums;

namespace FoodParkDigital.Domain.Fiscal.Entities;

/// <summary>
/// The CFDI 4.0 invoice (ingreso) of an order to a customer with an RFC, stamped through the platform's PAC with the
/// business's CSD (Fase 28A, ADR 0045). Its amounts are the ones the backend computed for the order's CFDI draft. A
/// cancelled one stays, and the order can be invoiced again.
/// </summary>
public class OrderInvoice : CfdiInvoice, IAggregateRoot
{
    private OrderInvoice() { }

    public Guid OrderId { get; private set; }

    public CfdiReceiver Receiver { get; private set; } = null!;

    /// <summary>
    /// The invoice of a paid order, with its folio and the amounts of its CFDI draft, before the PAC is asked: what is saved
    /// here is what every attempt sends.
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
        DateTimeOffset now)
    {
        if (tenantId == Guid.Empty || orderId == Guid.Empty || requestedByUserId == Guid.Empty)
            throw new DomainException("La factura necesita su negocio, su pedido y quién la pide.");
        ArgumentNullException.ThrowIfNull(receiver);

        var invoice = new OrderInvoice { OrderId = orderId, Receiver = receiver };
        invoice.Begin(tenantId, serie, folio, paymentForm, document, subTotal, taxTotal, total, requestedByUserId, now);
        return invoice;
    }

    /// <summary>Because it has errors and nothing replaces it (02), or because the sale did not happen (03).</summary>
    public override bool TakesCancellationMotive(CfdiCancellationMotive motive)
        => motive is CfdiCancellationMotive.ErrorsWithoutReplacement or CfdiCancellationMotive.NotCarriedOut;

    protected override string CancellationMotivesMessage
        => "Cancela por errores sin relación (02) o porque la venta no se llevó a cabo (03).";
}
