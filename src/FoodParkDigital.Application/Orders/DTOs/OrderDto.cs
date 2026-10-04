using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Orders.DTOs;

public class OrderDto
{
    public Guid Id { get; set; }
    public Guid? CustomerUserId { get; set; }
    public Guid? TemporaryCustomerId { get; set; }
    public bool CreatedByBusiness { get; set; }
    public string OrderNumber { get; set; } = null!;
    public BusinessType BusinessType { get; set; }
    public OrderType OrderType { get; set; }
    public OrderStatus Status { get; set; }
    public string CustomerName { get; set; } = null!;
    public string? CustomerPhone { get; set; }
    public string? CustomerNotes { get; set; }
    public string? TableNumber { get; set; }
    /// <summary>True when item prices include IVA; false when IVA was added on top.</summary>
    public bool PricesIncludeTax { get; set; }
    /// <summary>Taxable base: item amounts without IVA.</summary>
    public decimal SubTotal { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
    /// <summary>IVA by rate, computed by the server from the items (see Order.GetTaxBreakdown).</summary>
    public List<OrderTaxLineDto> TaxBreakdown { get; set; } = new();
    public decimal PaidAmount { get; set; }
    public decimal Balance { get; set; }
    /// <summary>Money given back; payments are not edited.</summary>
    public decimal RefundedAmount { get; set; }
    /// <summary>Paid minus refunded.</summary>
    public decimal NetPaidAmount { get; set; }
    public string? CancellationReason { get; set; }
    public bool CanCancel { get; set; }
    public List<OrderStatus> AllowedTransitions { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public List<OrderStatusHistoryDto> StatusHistory { get; set; } = new();
    public ServiceOrderDetailDto? ServiceDetail { get; set; }
    public List<ServiceOrderPieceDto> ServicePieces { get; set; } = new();
    public List<OrderPaymentDto> Payments { get; set; } = new();
    public List<OrderRefundDto> Refunds { get; set; } = new();
}

public class OrderRefundDto
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public OrderPaymentMethod Method { get; set; }
    public string Reason { get; set; } = null!;
    public DateTimeOffset RefundedAt { get; set; }
    public string RefundedBy { get; set; } = null!;
    public string? Reference { get; set; }
    public bool RestockItems { get; set; }
    public List<OrderRefundLineDto> Lines { get; set; } = new();
}

public class OrderRefundLineDto
{
    public Guid OrderItemId { get; set; }
    public int Quantity { get; set; }
}

public class OrderItemDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = null!;
    public int Quantity { get; set; }
    /// <summary>Units given back with refunds.</summary>
    public int ReturnedQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }
    public string? Notes { get; set; }
    public decimal TaxRate { get; set; }
    public bool IsTaxExempt { get; set; }
}

public class OrderTaxLineDto
{
    public decimal RatePercent { get; set; }
    public bool IsExempt { get; set; }
    public decimal TaxableBase { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
}

public class OrderStatusHistoryDto
{
    public Guid Id { get; set; }
    public OrderStatus PreviousStatus { get; set; }
    public OrderStatus NewStatus { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public string? ChangedBy { get; set; }
}

public class ServiceOrderDetailDto
{
    public Guid Id { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? PromisedAt { get; set; }
    public int Priority { get; set; }
    public string? IntakeNotes { get; set; }
    public string? DeliveryNotes { get; set; }
    public string? IntakeChecklistJson { get; set; }
    public string? BeforePhotosJson { get; set; }
    public string? AfterPhotosJson { get; set; }
}

public class ServiceOrderPieceDto
{
    public Guid Id { get; set; }
    public string ItemName { get; set; } = null!;
    public Guid? ServiceProductId { get; set; }
    public bool IsTrackable { get; set; }
    public bool NotifyCustomer { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public string? AssignedToName { get; set; }
    public DateTimeOffset? AssignedAt { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? Color { get; set; }
    public string? Size { get; set; }
    public string? Material { get; set; }
    public string? ConditionNotes { get; set; }
    public string? RequestedService { get; set; }
    public string? InternalNotes { get; set; }
    public ServiceOrderPieceStatus Status { get; set; }
}

public class OrderPaymentDto
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public OrderPaymentMethod Method { get; set; }
    public DateTimeOffset PaidAt { get; set; }
    public Guid? InventoryLocationId { get; set; }
    public string? EvidenceUrl { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
}

public class KitchenOrderDto
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = null!;
    public OrderStatus Status { get; set; }
    public string CustomerName { get; set; } = null!;
    public string? TableNumber { get; set; }
    public string? CustomerNotes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
}

public class DashboardMetricsDto
{
    public DateTimeOffset RangeFrom { get; set; }
    public DateTimeOffset RangeTo { get; set; }
    public int TotalOrders { get; set; }
    public int InProgressOrders { get; set; }
    public int PendingOrders { get; set; }
    public int CompletedOrders { get; set; }
    public int CancelledOrders { get; set; }
    public int UniqueCustomers { get; set; }
    public int ActiveProducts { get; set; }
    public int ActiveServices { get; set; }
    /// <summary>Payments registered in the range, whatever the status of their orders.</summary>
    public decimal TotalRevenue { get; set; }
    /// <summary>Revenue divided by the orders that received those payments.</summary>
    public decimal AverageOrderValue { get; set; }
    public int PaidOrders { get; set; }
    /// <summary>Balance still owed by the orders of the range that are not cancelled.</summary>
    public decimal OutstandingBalance { get; set; }
    public int UnpaidOrders { get; set; }
    public Dictionary<string, int> OrdersByStatus { get; set; } = new();
    public List<TopSellingItemDto> TopSellingItems { get; set; } = new();
}

public class TopSellingItemDto
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = null!;
    public string ItemType { get; set; } = "Product";
    public int QuantitySold { get; set; }
    public decimal TotalAmount { get; set; }
}
