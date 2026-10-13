using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Application.Couriers;

/// <summary>What a courier owes the business (Fase 22D, Caja › Repartidores) and what it carries on its way.</summary>
public class CourierReceivableDto
{
    public DeliveryCourier Courier { get; set; }
    /// <summary>Delivered orders it collected in cash and has not handed over.</summary>
    public int OwedOrders { get; set; }
    public decimal OwedAmount { get; set; }
    /// <summary>When the oldest of them went out.</summary>
    public DateTimeOffset? OldestDispatchedAt { get; set; }
    /// <summary>Orders on their way that it will collect.</summary>
    public int OnTheWayOrders { get; set; }
    public decimal OnTheWayAmount { get; set; }
    public DateTimeOffset? LastSettledAt { get; set; }
}

/// <summary>An order a courier collects: owed once delivered, on its way before.</summary>
public class CourierOrderDto
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = null!;
    public string CustomerName { get; set; } = null!;
    public string? CourierFolio { get; set; }
    public DateTimeOffset DispatchedAt { get; set; }
    public OrderStatus Status { get; set; }
    /// <summary>What the courier collects: the balance when it went out.</summary>
    public decimal CashToCollect { get; set; }
    public decimal? PaysWith { get; set; }
    /// <summary>What it owes now: the order's balance (what a settlement pays).</summary>
    public decimal Balance { get; set; }
}

public class PendingCourierOrdersDto
{
    /// <summary>Delivered and collected: what a settlement pays, the oldest first.</summary>
    public List<CourierOrderDto> Owed { get; set; } = new();
    /// <summary>On their way, not owed yet.</summary>
    public List<CourierOrderDto> OnTheWay { get; set; } = new();
    /// <summary>More orders are owed than one settlement takes; the oldest come first.</summary>
    public bool Truncated { get; set; }
}

public class CourierSettlementDto
{
    public Guid Id { get; set; }
    public DeliveryCourier Courier { get; set; }
    public DateTimeOffset SettledAt { get; set; }
    public string? SettledBy { get; set; }
    public OrderPaymentMethod Method { get; set; }
    public decimal HandedAmount { get; set; }
    public decimal OrdersTotal { get; set; }
    public decimal AdjustmentsTotal { get; set; }
    public int OrderCount { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
}

public class CourierSettlementOrderDto
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = null!;
    public string CustomerName { get; set; } = null!;
    public string? CourierFolio { get; set; }
    public decimal Amount { get; set; }
}

public class CourierSettlementAdjustmentDto
{
    public int Position { get; set; }
    public CourierSettlementAdjustmentKind Kind { get; set; }
    public string? Description { get; set; }
    public decimal Amount { get; set; }
}

public class CourierSettlementDetailDto
{
    public CourierSettlementDto Settlement { get; set; } = null!;
    public List<CourierSettlementOrderDto> Orders { get; set; } = new();
    public List<CourierSettlementAdjustmentDto> Adjustments { get; set; } = new();
}
