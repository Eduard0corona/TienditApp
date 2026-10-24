using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal.Enums;

namespace FoodParkDigital.Domain.Fiscal.Entities;

/// <summary>
/// The global invoice (CFDI of ingreso to the general public, with InformacionGlobal) of a period that is over (Fase 28B,
/// ADR 0046): one concept per sale paid in that period that has no invoice of its own, stamped through the platform's PAC
/// with the business's CSD. It keeps the sales it covers, so none of them is invoiced twice; refused or cancelled, they are
/// free again for another global invoice or an invoice of their own. It is never deleted.
/// </summary>
public class GlobalInvoice : CfdiInvoice, IAggregateRoot
{
    /// <summary>The most sales one global invoice covers: a CFDI the PAC still takes in one request.</summary>
    public const int MaxSales = 2_000;

    private readonly List<GlobalInvoiceOrder> _orders = [];

    private GlobalInvoice() { }

    public GlobalInvoicePeriodicity Periodicity { get; private set; }

    /// <summary>The first day of the period, in the business's calendar.</summary>
    public DateOnly PeriodFrom { get; private set; }

    /// <summary>The last day of the period, in the business's calendar.</summary>
    public DateOnly PeriodTo { get; private set; }

    /// <summary>c_Meses of its InformacionGlobal.</summary>
    public string Months { get; private set; } = null!;

    /// <summary>Año of its InformacionGlobal.</summary>
    public int Year { get; private set; }

    /// <summary>How many sales it covers (its concepts may be more: one per IVA rate of each sale).</summary>
    public int OrderCount { get; private set; }

    public IReadOnlyCollection<GlobalInvoiceOrder> Orders => _orders.AsReadOnly();

    /// <summary>
    /// The global invoice of a period that is over, with its folio and the CFDI built from its sales, before the PAC is
    /// asked: what is saved here is what every attempt sends. The sales' totals add up to its total.
    /// </summary>
    public static GlobalInvoice Request(
        Guid tenantId,
        GlobalInvoicePeriod period,
        string serie,
        long folio,
        string paymentForm,
        string document,
        decimal subTotal,
        decimal taxTotal,
        decimal total,
        IReadOnlyList<GlobalInvoiceSale> sales,
        Guid requestedByUserId,
        DateTimeOffset now)
    {
        if (tenantId == Guid.Empty || requestedByUserId == Guid.Empty)
            throw new DomainException("La factura global necesita su negocio y quién la pide.");
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(sales);
        if (sales.Count == 0)
            throw new DomainException("La factura global necesita al menos una venta.");
        if (sales.Count > MaxSales)
            throw new DomainException(TooManySalesMessage(sales.Count));
        if (sales.Select(sale => sale.OrderId).Distinct().Count() != sales.Count)
            throw new DomainException("Una venta va una sola vez en la factura global.");
        if (sales.Any(sale => sale.OrderId == Guid.Empty || string.IsNullOrWhiteSpace(sale.OrderNumber) || sale.Total <= 0))
            throw new DomainException("Cada venta de la factura global necesita su pedido, su folio y su importe.");
        if (sales.Sum(sale => sale.Total) != total)
            throw new DomainException("Las ventas no suman el total de la factura global.");

        var invoice = new GlobalInvoice
        {
            Periodicity = period.Periodicity,
            PeriodFrom = period.From,
            PeriodTo = period.To,
            Months = period.Months,
            Year = period.Year,
            OrderCount = sales.Count
        };
        invoice.Begin(tenantId, serie, folio, paymentForm, PaidInOneExhibition, document, subTotal, taxTotal, total, requestedByUserId, now);
        foreach (var sale in sales)
            invoice._orders.Add(new GlobalInvoiceOrder(tenantId, invoice.Id, sale));
        return invoice;
    }

    /// <summary>A month of a busy store may have more sales than one CFDI holds: a shorter period splits them.</summary>
    public static string TooManySalesMessage(int count)
        => $"El periodo tiene {count:N0} ventas y una factura global lleva hasta {MaxSales:N0}: usa una periodicidad más corta.";

    /// <summary>
    /// Because it has errors and nothing replaces it (02), or because a customer asked for the invoice of a sale it covered
    /// (04: it is cancelled, issued again without that sale, and the sale is invoiced to the customer).
    /// </summary>
    public override bool TakesCancellationMotive(CfdiCancellationMotive motive)
        => motive is CfdiCancellationMotive.ErrorsWithoutReplacement or CfdiCancellationMotive.NominativeInGlobalInvoice;

    protected override string CancellationMotivesMessage
        => "Cancela la factura global por errores sin relación (02) o porque un cliente pidió la factura de una de sus ventas (04).";

    /// <summary>Its sales are free again: for another global invoice of the period, or an invoice of their own.</summary>
    protected override void Released()
    {
        // Read without its sales, they would stay taken: whoever changes it loads them.
        if (_orders.Count != OrderCount)
            throw new InvalidOperationException("A global invoice is refused or cancelled with its sales loaded.");
        foreach (var order in _orders)
            order.Release();
    }
}

/// <summary>A sale that goes in a global invoice: its order, its folio and what it charged.</summary>
public sealed record GlobalInvoiceSale(Guid OrderId, string OrderNumber, decimal Total);

/// <summary>
/// A sale a global invoice covers (Fase 28B). While the invoice stands with the SAT the sale is taken: no other global
/// invoice and no invoice of its own covers it. A refused or cancelled global invoice releases it, and the row stays.
/// </summary>
public class GlobalInvoiceOrder : ITenantScoped
{
    public const int MaxOrderNumberLength = 50;

    private GlobalInvoiceOrder() { }

    internal GlobalInvoiceOrder(Guid tenantId, Guid globalInvoiceId, GlobalInvoiceSale sale)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        GlobalInvoiceId = globalInvoiceId;
        OrderId = sale.OrderId;
        OrderNumber = sale.OrderNumber.Trim();
        Total = sale.Total;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid GlobalInvoiceId { get; private set; }
    public Guid OrderId { get; private set; }

    /// <summary>The order's folio, as its concept names it (NoIdentificacion).</summary>
    public string OrderNumber { get; private set; } = null!;

    /// <summary>What the sale charged: the total of its concepts.</summary>
    public decimal Total { get; private set; }

    /// <summary>The global invoice no longer stands with the SAT (refused or cancelled): the sale is free again.</summary>
    public bool Released { get; private set; }

    internal void Release() => Released = true;
}
