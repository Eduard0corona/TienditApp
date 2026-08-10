using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Application.Orders.DTOs;

public class ServiceBoardPieceDto
{
    public Guid PieceId { get; set; }
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = null!;
    public string CustomerName { get; set; } = null!;
    public string? CustomerPhone { get; set; }
    public DateTimeOffset OrderCreatedAt { get; set; }
    public DateTimeOffset? PromisedAt { get; set; }
    public int Priority { get; set; }
    public string ItemName { get; set; } = null!;
    public string? RequestedService { get; set; }
    public string? InternalNotes { get; set; }
    public bool NotifyCustomer { get; set; }
    public ServiceOrderPieceStatus Status { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public string? AssignedToName { get; set; }
    public DateTimeOffset? AssignedAt { get; set; }
    public bool IsOverdue { get; set; }
    public int? DelayMinutes { get; set; }
    public int? MinutesToPromise { get; set; }
}
