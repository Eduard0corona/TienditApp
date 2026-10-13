using System.Globalization;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Orders.HomeDelivery;

/// <summary>
/// The cash a courier hands over for the orders it collected (Fase 22D, Caja › Repartidores): the orders it pays, each for
/// what it still owed, the adjustments that explain the rest (its trip, its fee for collecting, a shortage…) and what it
/// handed over, which must be exactly the orders plus the adjustments. It is the moment the money reaches the business, so
/// the use case registers each order's payment and each adjustment in the drawer of whoever receives it. It never changes
/// once saved: a mistake is compensated with a refund or a movement of the drawer, never by editing or deleting it.
/// </summary>
public class CourierSettlement : AuditableEntity, ITenantScoped, IAggregateRoot
{
    /// <summary>The orders of one handover: a courier's day, with room to spare.</summary>
    public const int MaxOrders = 200;
    public const int MaxAdjustments = 50;
    /// <summary>The largest amount, handed over or of one order or adjustment: a guard against a figure typed wrong.</summary>
    public const decimal MaxAmount = 1_000_000m;
    public const int MaxReferenceLength = 60;
    public const int MaxNotesLength = 500;

    public const string TenantRequiredMessage = "La liquidación necesita su negocio.";
    public const string CourierMessage = "Elige qué repartidor entrega el efectivo.";
    public const string DrawerRequiredMessage = "La liquidación entra a una caja abierta.";
    public const string MethodMessage = "El repartidor entrega en efectivo o por transferencia.";
    public const string HandedAmountMessage = "Lo que entregó va de $0 a $1,000,000, con dos decimales.";
    public const string NoOrdersMessage = "Elige al menos un pedido que cobró el repartidor.";
    public const string TooManyOrdersMessage = "Una liquidación lleva hasta 200 pedidos.";
    public const string RepeatedOrderMessage = "Un pedido está repetido en la liquidación.";
    public const string OrderAmountMessage = "Lo que debe cada pedido va de $0.01 a $1,000,000, con dos decimales.";
    public const string TooManyAdjustmentsMessage = "Una liquidación lleva hasta 50 ajustes.";
    public const string AdjustmentKindMessage = "Elige el motivo de cada ajuste.";
    public const string AdjustmentAmountMessage = "Cada ajuste va de -$1,000,000 a $1,000,000, sin ser $0, con dos decimales.";
    public const string DeductionMessage = "Un viaje, una comisión o un faltante se descuentan: van en negativo.";
    public const string AdjustmentDescriptionRequiredMessage = "Un ajuste «Otro» necesita una descripción.";
    public const string AdjustmentDescriptionTooLongMessage = "La descripción de un ajuste admite hasta 200 caracteres.";
    public const string ReferenceTooLongMessage = "La referencia admite hasta 60 caracteres.";
    public const string NotesTooLongMessage = "Las notas admiten hasta 500 caracteres.";

    private readonly List<CourierSettlementOrder> _orders = new();
    private readonly List<CourierSettlementAdjustment> _adjustments = new();

    private CourierSettlement() { }

    /// <param name="cashSessionId">The open drawer of whoever receives the money.</param>
    /// <param name="method">How the courier handed it over: cash or a transfer.</param>
    public CourierSettlement(
        Guid tenantId,
        DeliveryCourier courier,
        Guid cashSessionId,
        OrderPaymentMethod method,
        decimal handedAmount,
        IEnumerable<CourierSettlementOrderInput> orders,
        IEnumerable<CourierSettlementAdjustmentInput> adjustments,
        string? reference,
        string? notes,
        DateTimeOffset settledAt,
        string? settledBy)
    {
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(adjustments);
        if (tenantId == Guid.Empty)
            throw new DomainException(TenantRequiredMessage);
        if (!Enum.IsDefined(courier))
            throw new DomainException(CourierMessage);
        if (cashSessionId == Guid.Empty)
            throw new DomainException(DrawerRequiredMessage);
        if (method is not (OrderPaymentMethod.Cash or OrderPaymentMethod.Transfer))
            throw new DomainException(MethodMessage);
        if (handedAmount < 0 || !IsMoney(handedAmount))
            throw new DomainException(HandedAmountMessage);

        var orderList = orders.ToList();
        var adjustmentList = adjustments.Select(a => a with { Description = Normalize(a.Description) }).ToList();
        CheckOrders(orderList);
        CheckAdjustments(adjustmentList);
        reference = Normalize(reference);
        notes = Normalize(notes);
        if (reference is { Length: > MaxReferenceLength })
            throw new DomainException(ReferenceTooLongMessage);
        if (notes is { Length: > MaxNotesLength })
            throw new DomainException(NotesTooLongMessage);

        var ordersTotal = orderList.Sum(o => o.Amount);
        var adjustmentsTotal = adjustmentList.Sum(a => a.Amount);
        if (handedAmount != ordersTotal + adjustmentsTotal)
            throw new DomainException(DoesNotBalanceMessage(handedAmount, ordersTotal + adjustmentsTotal));

        TenantId = tenantId;
        Courier = courier;
        CashSessionId = cashSessionId;
        Method = method;
        HandedAmount = handedAmount;
        OrdersTotal = ordersTotal;
        AdjustmentsTotal = adjustmentsTotal;
        Reference = reference;
        Notes = notes;
        SettledAt = settledAt;
        SettledBy = settledBy;
        _orders.AddRange(orderList.Select(o => new CourierSettlementOrder(tenantId, Id, o.OrderId, o.Amount)));
        _adjustments.AddRange(adjustmentList.Select((a, index) =>
            new CourierSettlementAdjustment(tenantId, Id, index + 1, a.Kind, a.Description, a.Amount)));
    }

    public Guid TenantId { get; private set; }
    public DeliveryCourier Courier { get; private set; }
    /// <summary>The drawer the money entered.</summary>
    public Guid CashSessionId { get; private set; }
    public OrderPaymentMethod Method { get; private set; }
    /// <summary>What the courier handed over: the orders plus the adjustments, to the cent.</summary>
    public decimal HandedAmount { get; private set; }
    public decimal OrdersTotal { get; private set; }
    /// <summary>The adjustments added up: negative when the courier deducted from the cash.</summary>
    public decimal AdjustmentsTotal { get; private set; }
    public string? Reference { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset SettledAt { get; private set; }
    public string? SettledBy { get; private set; }

    public IReadOnlyCollection<CourierSettlementOrder> Orders => _orders.AsReadOnly();
    public IReadOnlyList<CourierSettlementAdjustment> Adjustments => _adjustments.OrderBy(a => a.Position).ToList();

    /// <summary>«Entregó $500.00 y los pedidos menos los descuentos dan $480.00: la diferencia debe ser $0.00.»</summary>
    public static string DoesNotBalanceMessage(decimal handedAmount, decimal expected)
        => string.Create(CultureInfo.InvariantCulture,
            $"Entregó ${handedAmount:N2} y los pedidos con sus ajustes dan ${expected:N2}: la diferencia debe ser $0.00. Registra lo que falta como un ajuste.");

    /// <summary>Whether a kind is deducted from the cash (negative): every kind but «Otro».</summary>
    public static bool IsDeduction(CourierSettlementAdjustmentKind kind) => kind != CourierSettlementAdjustmentKind.Other;

    private static void CheckOrders(IReadOnlyList<CourierSettlementOrderInput> orders)
    {
        if (orders.Count == 0)
            throw new DomainException(NoOrdersMessage);
        if (orders.Count > MaxOrders)
            throw new DomainException(TooManyOrdersMessage);
        if (orders.Any(o => o.OrderId == Guid.Empty) || orders.Select(o => o.OrderId).Distinct().Count() != orders.Count)
            throw new DomainException(RepeatedOrderMessage);
        if (orders.Any(o => o.Amount <= 0 || !IsMoney(o.Amount)))
            throw new DomainException(OrderAmountMessage);
    }

    private static void CheckAdjustments(IReadOnlyList<CourierSettlementAdjustmentInput> adjustments)
    {
        if (adjustments.Count > MaxAdjustments)
            throw new DomainException(TooManyAdjustmentsMessage);

        foreach (var adjustment in adjustments)
        {
            if (!Enum.IsDefined(adjustment.Kind))
                throw new DomainException(AdjustmentKindMessage);
            if (adjustment.Amount == 0 || !IsMoney(adjustment.Amount))
                throw new DomainException(AdjustmentAmountMessage);
            if (IsDeduction(adjustment.Kind) && adjustment.Amount > 0)
                throw new DomainException(DeductionMessage);
            if (adjustment.Kind == CourierSettlementAdjustmentKind.Other && adjustment.Description is null)
                throw new DomainException(AdjustmentDescriptionRequiredMessage);
            if (adjustment.Description is { Length: > CourierSettlementAdjustment.MaxDescriptionLength })
                throw new DomainException(AdjustmentDescriptionTooLongMessage);
        }
    }

    /// <summary>Two decimals at most and no further from zero than <see cref="MaxAmount"/>.</summary>
    private static bool IsMoney(decimal amount) => Math.Abs(amount) <= MaxAmount && decimal.Round(amount, 2) == amount;

    private static string? Normalize(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

/// <summary>An order a settlement pays, with what the courier still owed for it. An order is paid by one settlement at most.</summary>
public class CourierSettlementOrder : BaseEntity, ITenantScoped
{
    private CourierSettlementOrder() { }

    internal CourierSettlementOrder(Guid tenantId, Guid settlementId, Guid orderId, decimal amount)
    {
        TenantId = tenantId;
        SettlementId = settlementId;
        OrderId = orderId;
        Amount = amount;
    }

    public Guid TenantId { get; private set; }
    public Guid SettlementId { get; private set; }
    public Guid OrderId { get; private set; }

    /// <summary>The order's balance when the courier handed it over: the payment the settlement registered on it.</summary>
    public decimal Amount { get; private set; }
}

/// <summary>Part of a handover that no order explains: the courier's trip or fee, a shortage… with its signed amount.</summary>
public class CourierSettlementAdjustment : BaseEntity, ITenantScoped
{
    public const int MaxDescriptionLength = 200;

    private CourierSettlementAdjustment() { }

    internal CourierSettlementAdjustment(
        Guid tenantId, Guid settlementId, int position, CourierSettlementAdjustmentKind kind, string? description, decimal amount)
    {
        TenantId = tenantId;
        SettlementId = settlementId;
        Position = position;
        Kind = kind;
        Description = description;
        Amount = amount;
    }

    public Guid TenantId { get; private set; }
    public Guid SettlementId { get; private set; }

    /// <summary>Its place in the settlement, from 1: the adjustments come back in the order they were written.</summary>
    public int Position { get; private set; }

    public CourierSettlementAdjustmentKind Kind { get; private set; }
    public string? Description { get; private set; }

    /// <summary>Negative when the courier deducted it from the cash, positive when it added it.</summary>
    public decimal Amount { get; private set; }
}

/// <summary>An order chosen for a settlement, with what the use case read it still owes.</summary>
public sealed record CourierSettlementOrderInput(Guid OrderId, decimal Amount);

/// <summary>An adjustment as typed: the reason, a description and the signed amount.</summary>
public sealed record CourierSettlementAdjustmentInput(CourierSettlementAdjustmentKind Kind, string? Description, decimal Amount);
