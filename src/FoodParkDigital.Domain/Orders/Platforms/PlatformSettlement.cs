using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Orders.Platforms;

/// <summary>
/// One deposit of a delivery platform reconciled against its orders (Reportes › Liquidaciones): the period it pays, what
/// arrived, the orders it pays with the expected net of each and the adjustments that explain the rest. It closes only
/// when every peso is explained, a closed one never changes, and a mistake is voided with a reason (never deleted), which
/// frees its orders for another settlement.
/// </summary>
public class PlatformSettlement : AuditableEntity, ITenantScoped, IAggregateRoot
{
    /// <summary>The longest period one deposit pays: a platform settles every week, a catch-up a month at most.</summary>
    public const int MaxPeriodDays = 31;
    /// <summary>The earliest date a period starts on: a guard against a year typed wrong.</summary>
    public static readonly DateOnly EarliestPeriodFrom = new(2000, 1, 1);
    /// <summary>The largest deposit either way: a guard against a figure typed wrong.</summary>
    public const decimal MaxDepositedAmount = 10_000_000m;
    public const int MaxReferenceLength = 60;
    public const int MaxNotesLength = 500;
    public const int MaxVoidReasonLength = 500;
    /// <summary>The orders of one settlement: about 285 a day for a week.</summary>
    public const int MaxOrders = 2_000;
    public const int MaxAdjustments = 100;

    public const string PeriodOrderMessage = "El periodo termina antes de empezar.";
    public const string PeriodTooLongMessage = "El periodo puede tener hasta 31 días.";
    public const string PeriodInTheFutureMessage = "El periodo no puede terminar después de hoy.";
    public const string PeriodTooOldMessage = "Revisa el año del periodo: no puede empezar antes de 2000.";
    public const string DepositBeforePeriodMessage = "La fecha del depósito no puede ser anterior al periodo.";
    public const string DepositInTheFutureMessage = "La fecha del depósito no puede ser futura.";
    public const string DepositedAmountMessage = "Lo depositado va de -$10,000,000 a $10,000,000, con dos decimales.";
    public const string ReferenceTooLongMessage = "La referencia admite hasta 60 caracteres.";
    public const string NotesTooLongMessage = "Las notas admiten hasta 500 caracteres.";
    public const string TooManyOrdersMessage = "Una liquidación lleva hasta 2,000 pedidos.";
    public const string RepeatedOrderMessage = "Un pedido está repetido en la liquidación.";
    public const string OrderOfAnotherPlatformMessage = "Un pedido de otra plataforma no entra en esta liquidación.";
    public const string OrderAfterThePeriodMessage = "Un pedido registrado después del periodo no entra en esta liquidación.";
    public const string OrderBringsNothingMessage =
        "Un pedido que no trae nada (devuelto completo, o que el negocio todavía no cobra) no entra en esta liquidación.";
    public const string TooManyAdjustmentsMessage = "Una liquidación lleva hasta 100 ajustes.";
    public const string AdjustmentKindMessage = "Elige el motivo de cada ajuste.";
    public const string AdjustmentAmountMessage = "Cada ajuste va de -$1,000,000 a $1,000,000, sin ser $0, con dos decimales.";
    public const string AdjustmentDescriptionRequiredMessage = "Un ajuste «Otro» necesita una descripción.";
    public const string AdjustmentDescriptionTooLongMessage = "La descripción de un ajuste admite hasta 200 caracteres.";
    public const string ClosedMessage = "Una liquidación cerrada ya no cambia: anúlala y registra otra.";
    public const string VoidedMessage = "Esta liquidación está anulada: ya no cambia.";
    public const string NothingToCloseMessage = "Agrega al menos un pedido o un ajuste antes de cerrar.";
    public const string DoesNotBalanceMessage = "La liquidación no cuadra: la diferencia debe ser $0.00. Registra lo que falta como un ajuste.";
    public const string VoidReasonRequiredMessage = "Escribe por qué anulas la liquidación.";
    public const string VoidReasonTooLongMessage = "El motivo admite hasta 500 caracteres.";

    private readonly List<PlatformSettlementOrder> _orders = new();
    private readonly List<PlatformSettlementAdjustment> _adjustments = new();

    private PlatformSettlement() { }

    /// <param name="today">The date of the business now: neither the period nor the deposit reach the future.</param>
    public PlatformSettlement(
        Guid tenantId,
        OrderChannel channel,
        PlatformSettlementDeposit deposit,
        IEnumerable<PlatformSettlementOrderInput> orders,
        IEnumerable<PlatformSettlementAdjustmentInput> adjustments,
        DateOnly today)
    {
        if (tenantId == Guid.Empty)
            throw new DomainException(DeliveryPlatformSetting.TenantRequiredMessage);
        if (!OrderChannelRules.IsDeliveryPlatform(channel))
            throw new DomainException(DeliveryPlatformSetting.NotADeliveryPlatformMessage);

        TenantId = tenantId;
        Channel = channel;
        Status = PlatformSettlementStatus.Open;
        Apply(deposit, orders, adjustments, today);
        Revision = 1;
    }

    public Guid TenantId { get; private set; }

    /// <summary>The platform whose deposit this is. It never changes.</summary>
    public OrderChannel Channel { get; private set; }

    public DateOnly PeriodFrom { get; private set; }
    public DateOnly PeriodTo { get; private set; }
    public DateOnly DepositedOn { get; private set; }

    /// <summary>What arrived; negative when the platform charged the business instead.</summary>
    public decimal DepositedAmount { get; private set; }

    public string? Reference { get; private set; }
    public string? Notes { get; private set; }
    public PlatformSettlementStatus Status { get; private set; }

    /// <summary>The sum of its orders' expected net, kept with them.</summary>
    public decimal OrdersTotal { get; private set; }

    /// <summary>The sum of its adjustments, kept with them.</summary>
    public decimal AdjustmentsTotal { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }
    public string? VoidReason { get; private set; }

    /// <summary>Starts at 1 and goes up by one with every change; a change with an older one is refused.</summary>
    public int Revision { get; private set; }

    public IReadOnlyCollection<PlatformSettlementOrder> Orders => _orders.AsReadOnly();

    /// <summary>In the order they were written, whatever order the database read them in.</summary>
    public IReadOnlyCollection<PlatformSettlementAdjustment> Adjustments
        => _adjustments.OrderBy(adjustment => adjustment.Position).ToList().AsReadOnly();

    /// <summary>What the platform should have deposited: its orders plus its adjustments.</summary>
    public decimal ExpectedDeposit => OrdersTotal + AdjustmentsTotal;

    /// <summary>What arrived minus what should have: it closes only at zero.</summary>
    public decimal Difference => DepositedAmount - ExpectedDeposit;

    /// <summary>Replaces the deposit, the orders and the adjustments of an open settlement, whole.</summary>
    public void Update(
        PlatformSettlementDeposit deposit,
        IEnumerable<PlatformSettlementOrderInput> orders,
        IEnumerable<PlatformSettlementAdjustmentInput> adjustments,
        DateOnly today)
    {
        EnsureOpen();
        Apply(deposit, orders, adjustments, today);
        Revision++;
    }

    /// <summary>
    /// Counts again the expected net of its orders (a refund may have changed one since the last save).
    /// </summary>
    /// <param name="expectedNets">The expected net of each of its orders now.</param>
    /// <returns>True, with a new revision, when one changed: the person reviews the new figures before closing.</returns>
    public bool Recount(IReadOnlyDictionary<Guid, decimal> expectedNets)
    {
        ArgumentNullException.ThrowIfNull(expectedNets);
        EnsureOpen();

        var changed = false;
        foreach (var line in _orders)
        {
            if (!expectedNets.TryGetValue(line.OrderId, out var expectedNet))
                throw new ArgumentException($"The expected net of order {line.OrderId} is missing.", nameof(expectedNets));
            if (line.ExpectedNet == expectedNet)
                continue;
            line.Recount(expectedNet);
            changed = true;
        }

        if (!changed)
            return false;
        OrdersTotal = _orders.Sum(line => line.ExpectedNet);
        Revision++;
        return true;
    }

    /// <summary>Closes the settlement once every peso of the deposit is explained. It no longer changes.</summary>
    public void Close(DateTimeOffset closedAt)
    {
        EnsureOpen();
        if (_orders.Count == 0 && _adjustments.Count == 0)
            throw new DomainException(NothingToCloseMessage);
        if (Difference != 0)
            throw new DomainException(DoesNotBalanceMessage);

        Status = PlatformSettlementStatus.Closed;
        ClosedAt = closedAt;
        Revision++;
    }

    /// <summary>Voids an open or closed settlement with a reason: kept as history, its orders pending again.</summary>
    public void Void(string? reason, DateTimeOffset voidedAt)
    {
        if (Status == PlatformSettlementStatus.Voided)
            throw new DomainException(VoidedMessage);
        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            throw new DomainException(VoidReasonRequiredMessage);
        if (trimmed.Length > MaxVoidReasonLength)
            throw new DomainException(VoidReasonTooLongMessage);

        foreach (var line in _orders)
            line.Release();
        Status = PlatformSettlementStatus.Voided;
        VoidedAt = voidedAt;
        VoidReason = trimmed;
        Revision++;
    }

    private void EnsureOpen()
    {
        if (Status == PlatformSettlementStatus.Closed)
            throw new DomainException(ClosedMessage);
        if (Status == PlatformSettlementStatus.Voided)
            throw new DomainException(VoidedMessage);
    }

    private void Apply(
        PlatformSettlementDeposit deposit,
        IEnumerable<PlatformSettlementOrderInput> orders,
        IEnumerable<PlatformSettlementAdjustmentInput> adjustments,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(deposit);
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(adjustments);

        // Everything is checked before anything changes: a refused save leaves the settlement as it was.
        var reference = Normalize(deposit.Reference);
        var notes = Normalize(deposit.Notes);
        CheckDeposit(deposit, reference, notes, today);
        var chosen = orders.ToList();
        CheckOrders(chosen, deposit.PeriodTo);
        var typed = adjustments
            .Select(adjustment => adjustment with { Description = Normalize(adjustment.Description) })
            .ToList();
        CheckAdjustments(typed);

        PeriodFrom = deposit.PeriodFrom;
        PeriodTo = deposit.PeriodTo;
        DepositedOn = deposit.DepositedOn;
        DepositedAmount = deposit.DepositedAmount;
        Reference = reference;
        Notes = notes;

        // The orders that stay keep their row (its expected net counted again); the rest leave and the new ones come in.
        var byOrder = chosen.ToDictionary(order => order.OrderId);
        _orders.RemoveAll(line => !byOrder.ContainsKey(line.OrderId));
        foreach (var line in _orders)
            line.Recount(byOrder[line.OrderId].ExpectedNet);
        var kept = _orders.Select(line => line.OrderId).ToHashSet();
        foreach (var order in chosen.Where(order => !kept.Contains(order.OrderId)))
            _orders.Add(new PlatformSettlementOrder(TenantId, Id, order.OrderId, order.ExpectedNet));

        _adjustments.Clear();
        foreach (var (adjustment, index) in typed.Select((adjustment, index) => (adjustment, index)))
            _adjustments.Add(new PlatformSettlementAdjustment(TenantId, Id, index + 1, adjustment.Kind, adjustment.Description, adjustment.Amount));

        OrdersTotal = _orders.Sum(line => line.ExpectedNet);
        AdjustmentsTotal = _adjustments.Sum(adjustment => adjustment.Amount);
    }

    private static void CheckDeposit(PlatformSettlementDeposit deposit, string? reference, string? notes, DateOnly today)
    {
        if (deposit.PeriodTo < deposit.PeriodFrom)
            throw new DomainException(PeriodOrderMessage);
        if (deposit.PeriodTo.DayNumber - deposit.PeriodFrom.DayNumber + 1 > MaxPeriodDays)
            throw new DomainException(PeriodTooLongMessage);
        if (deposit.PeriodFrom < EarliestPeriodFrom)
            throw new DomainException(PeriodTooOldMessage);
        if (deposit.PeriodTo > today)
            throw new DomainException(PeriodInTheFutureMessage);
        if (deposit.DepositedOn < deposit.PeriodFrom)
            throw new DomainException(DepositBeforePeriodMessage);
        if (deposit.DepositedOn > today)
            throw new DomainException(DepositInTheFutureMessage);
        if (!IsMoney(deposit.DepositedAmount, MaxDepositedAmount))
            throw new DomainException(DepositedAmountMessage);
        if (reference is { Length: > MaxReferenceLength })
            throw new DomainException(ReferenceTooLongMessage);
        if (notes is { Length: > MaxNotesLength })
            throw new DomainException(NotesTooLongMessage);
    }

    private void CheckOrders(IReadOnlyCollection<PlatformSettlementOrderInput> orders, DateOnly periodTo)
    {
        if (orders.Count > MaxOrders)
            throw new DomainException(TooManyOrdersMessage);
        if (orders.Select(order => order.OrderId).Distinct().Count() != orders.Count)
            throw new DomainException(RepeatedOrderMessage);
        var kept = _orders.Select(line => line.OrderId).ToHashSet();
        foreach (var order in orders)
        {
            if (order.OrderId == Guid.Empty)
                throw new ArgumentException("An order of a settlement needs its id.", nameof(orders));
            if (order.Channel != Channel)
                throw new DomainException(OrderOfAnotherPlatformMessage);
            if (order.RegisteredOn > periodTo)
                throw new DomainException(OrderAfterThePeriodMessage);
            if (decimal.Round(order.ExpectedNet, 2) != order.ExpectedNet)
                throw new ArgumentException("An expected net comes in cents.", nameof(orders));
            // Nothing tells «given back whole» from «not collected yet»: settled at 0, what the second brings later would
            // never be pending again. One already in the settlement stays at 0, which shows the refund.
            if (order.ExpectedNet == 0 && !kept.Contains(order.OrderId))
                throw new DomainException(OrderBringsNothingMessage);
        }
    }

    private static void CheckAdjustments(IReadOnlyCollection<PlatformSettlementAdjustmentInput> adjustments)
    {
        if (adjustments.Count > MaxAdjustments)
            throw new DomainException(TooManyAdjustmentsMessage);
        foreach (var adjustment in adjustments)
        {
            if (!Enum.IsDefined(adjustment.Kind))
                throw new DomainException(AdjustmentKindMessage);
            if (adjustment.Amount == 0 || !IsMoney(adjustment.Amount, PlatformSettlementAdjustment.MaxAmount))
                throw new DomainException(AdjustmentAmountMessage);
            if (adjustment.Kind == PlatformSettlementAdjustmentKind.Other && adjustment.Description is null)
                throw new DomainException(AdjustmentDescriptionRequiredMessage);
            if (adjustment.Description is { Length: > PlatformSettlementAdjustment.MaxDescriptionLength })
                throw new DomainException(AdjustmentDescriptionTooLongMessage);
        }
    }

    /// <summary>Two decimals at most and no further from zero than <paramref name="max"/>.</summary>
    private static bool IsMoney(decimal amount, decimal max) => Math.Abs(amount) <= max && decimal.Round(amount, 2) == amount;

    private static string? Normalize(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
