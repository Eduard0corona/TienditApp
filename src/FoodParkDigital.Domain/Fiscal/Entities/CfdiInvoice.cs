using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Fiscal.Entities;

/// <summary>
/// What every CFDI of ingreso a business stamps through the platform's PAC has (Fases 28A to 28C, ADR 0045 to 0047): the
/// invoice of an order and the global invoice of a period. Its amounts are the ones the backend computed; it is paid in
/// one exhibition (PUE), or in installments (PPD, only an order's invoice), each payment with its complement.
/// </summary>
public abstract class CfdiInvoice : Cfdi
{
    public const string DefaultSerie = "F";

    /// <summary>c_MetodoPago: what the invoice covers was paid in full when it was issued.</summary>
    public const string PaidInOneExhibition = "PUE";

    /// <summary>c_MetodoPago: paid in installments or later; each payment gets its complement (Fase 28C).</summary>
    public const string PaidInInstallments = "PPD";

    /// <summary>c_FormaPago: the method that paid the most, or «Por definir» (99) for an invoice in installments.</summary>
    public string PaymentForm { get; protected set; } = null!;

    /// <summary>c_MetodoPago: PUE, or PPD.</summary>
    public string PaymentMethod { get; protected set; } = null!;

    public decimal SubTotal { get; protected set; }
    public decimal TaxTotal { get; protected set; }
    public decimal Total { get; protected set; }

    public Guid RequestedByUserId { get; protected set; }

    /// <summary>Whether it is paid in installments, so each of its payments gets a complement.</summary>
    public bool IsPaidInInstallments => PaymentMethod == PaidInInstallments;

    /// <summary>
    /// What every invoice fixes before the PAC is asked (<see cref="Cfdi.Begin"/>), and how it is paid: in one exhibition with
    /// the form that paid it, or in installments with «Por definir» (99). The amounts add up.
    /// </summary>
    protected void Begin(
        Guid tenantId,
        string serie,
        long folio,
        string paymentForm,
        string paymentMethod,
        string document,
        decimal subTotal,
        decimal taxTotal,
        decimal total,
        Guid requestedByUserId,
        DateTimeOffset now)
    {
        switch (paymentMethod)
        {
            case PaidInOneExhibition
                when string.IsNullOrWhiteSpace(paymentForm) || paymentForm.Length != 2 || paymentForm == SatCatalog.PaymentFormToBeDefined:
                throw new DomainException("Una venta pagada se factura con la forma en que se pagó.");
            case PaidInInstallments when paymentForm != SatCatalog.PaymentFormToBeDefined:
                throw new DomainException("Una factura en parcialidades (PPD) lleva la forma de pago «Por definir» (99).");
            case PaidInOneExhibition or PaidInInstallments:
                break;
            default:
                throw new DomainException("La factura se paga en una exhibición (PUE) o en parcialidades (PPD).");
        }
        if (subTotal < 0 || taxTotal < 0 || total <= 0 || subTotal + taxTotal != total || !IsCents(total) || !IsCents(subTotal))
            throw new DomainException("Los importes de la factura no cuadran.");

        Begin(tenantId, serie, folio, document, now);
        PaymentForm = paymentForm;
        PaymentMethod = paymentMethod;
        SubTotal = subTotal;
        TaxTotal = taxTotal;
        Total = total;
        RequestedByUserId = requestedByUserId;
    }
}
