using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Orders.Entities;

/// <summary>
/// Units of a dish of an open tab cancelled after it went to the kitchen. The line keeps what was ordered: this record
/// is what compensates it, with the reason, who cancelled and when, and whether its supplies went back to stock (a dish
/// already prepared is lost, not returned).
/// </summary>
public class OrderItemCancellation : BaseEntity, ITenantScoped
{
    public const int MaxReasonLength = 500;
    public const int MaxCancelledByLength = 256;

    private OrderItemCancellation() { }

    internal OrderItemCancellation(
        Guid tenantId,
        Guid orderId,
        Guid orderItemId,
        int round,
        decimal quantity,
        decimal amount,
        string reason,
        bool returnedToStock,
        DateTimeOffset cancelledAt,
        string cancelledBy)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 3)
            throw new DomainException("Escribe por qué se cancela el platillo.");
        if (string.IsNullOrWhiteSpace(cancelledBy))
            throw new DomainException("La cancelación necesita a quien la hace.");

        TenantId = tenantId;
        OrderId = orderId;
        OrderItemId = orderItemId;
        Round = round;
        Quantity = quantity;
        Amount = amount;
        Reason = reason.Trim().Length > MaxReasonLength ? reason.Trim()[..MaxReasonLength] : reason.Trim();
        ReturnedToStock = returnedToStock;
        CancelledAt = cancelledAt;
        CancelledBy = cancelledBy.Trim().Length > MaxCancelledByLength ? cancelledBy.Trim()[..MaxCancelledByLength] : cancelledBy.Trim();
    }

    public Guid TenantId { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid OrderItemId { get; private set; }

    /// <summary>Round of the tab the dish was sent in.</summary>
    public int Round { get; private set; }

    public decimal Quantity { get; private set; }

    /// <summary>What the cancelled units took off the line, at its prices (before IVA is added, as the line total).</summary>
    public decimal Amount { get; private set; }

    public string Reason { get; private set; } = null!;

    /// <summary>The recipe supplies of the cancelled units went back to stock; otherwise they were lost.</summary>
    public bool ReturnedToStock { get; private set; }

    public DateTimeOffset CancelledAt { get; private set; }
    public string CancelledBy { get; private set; } = null!;
}
