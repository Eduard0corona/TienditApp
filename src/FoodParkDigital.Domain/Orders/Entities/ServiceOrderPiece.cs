using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Orders.Entities;

public class ServiceOrderPiece : AuditableEntity, ITenantScoped
{
    private ServiceOrderPiece() { }

    public ServiceOrderPiece(
        Guid tenantId,
        Guid orderId,
        string itemName,
        string? brand,
        string? model,
        string? color,
        string? size,
        string? material,
        string? conditionNotes,
        string? requestedService,
        string? internalNotes)
    {
        TenantId = tenantId;
        OrderId = orderId;
        ItemName = itemName;
        Brand = brand;
        Model = model;
        Color = color;
        Size = size;
        Material = material;
        ConditionNotes = conditionNotes;
        RequestedService = requestedService;
        InternalNotes = internalNotes;
        Status = ServiceOrderPieceStatus.Received;
    }

    public Guid TenantId { get; private set; }
    public Guid OrderId { get; private set; }
    public string ItemName { get; private set; } = null!;
    public string? Brand { get; private set; }
    public string? Model { get; private set; }
    public string? Color { get; private set; }
    public string? Size { get; private set; }
    public string? Material { get; private set; }
    public string? ConditionNotes { get; private set; }
    public string? RequestedService { get; private set; }
    public string? InternalNotes { get; private set; }
    public ServiceOrderPieceStatus Status { get; private set; }

    public Order Order { get; private set; } = null!;

    public void UpdateStatus(ServiceOrderPieceStatus status)
    {
        Status = status;
    }
}
