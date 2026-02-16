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
    public OrderStatus Status { get; set; }
    public string CustomerName { get; set; } = null!;
    public string? CustomerPhone { get; set; }
    public string? CustomerNotes { get; set; }
    public string? TableNumber { get; set; }
    public decimal SubTotal { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
    public string? CancellationReason { get; set; }
    public bool CanCancel { get; set; }
    public List<OrderStatus> AllowedTransitions { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public List<OrderStatusHistoryDto> StatusHistory { get; set; } = new();
}

public class OrderItemDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = null!;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }
    public string? Notes { get; set; }
}

public class OrderStatusHistoryDto
{
    public Guid Id { get; set; }
    public OrderStatus PreviousStatus { get; set; }
    public OrderStatus NewStatus { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public string? ChangedBy { get; set; }
}

public class KitchenOrderDto
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = null!;
    public OrderStatus Status { get; set; }
    public string CustomerName { get; set; } = null!;
    public string? TableNumber { get; set; }
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
    public decimal TotalRevenue { get; set; }
    public decimal AverageOrderValue { get; set; }
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
