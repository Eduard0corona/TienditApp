using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Orders.Entities;

public class ServiceOrderPiece : AuditableEntity, ITenantScoped
{
    private static readonly Dictionary<ServiceOrderPieceStatus, ServiceOrderPieceStatus[]> AllowedTransitions = new()
    {
        { ServiceOrderPieceStatus.Received, [ServiceOrderPieceStatus.Inspection, ServiceOrderPieceStatus.Cancelled] },
        { ServiceOrderPieceStatus.Inspection, [ServiceOrderPieceStatus.Washing, ServiceOrderPieceStatus.Cancelled] },
        { ServiceOrderPieceStatus.Washing, [ServiceOrderPieceStatus.Drying, ServiceOrderPieceStatus.Cancelled] },
        { ServiceOrderPieceStatus.Drying, [ServiceOrderPieceStatus.Detailing, ServiceOrderPieceStatus.Cancelled] },
        { ServiceOrderPieceStatus.Detailing, [ServiceOrderPieceStatus.Ready, ServiceOrderPieceStatus.Cancelled] },
        { ServiceOrderPieceStatus.Ready, [ServiceOrderPieceStatus.Delivered, ServiceOrderPieceStatus.Cancelled] },
        { ServiceOrderPieceStatus.Delivered, [] },
        { ServiceOrderPieceStatus.Cancelled, [] }
    };

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

    public IReadOnlyList<ServiceOrderPieceStatus> GetAllowedTransitions()
        => AllowedTransitions.TryGetValue(Status, out var transitions)
            ? transitions
            : Array.Empty<ServiceOrderPieceStatus>();

    public void UpdateStatus(ServiceOrderPieceStatus status)
    {
        if (!GetAllowedTransitions().Contains(status))
            throw new DomainException(
                $"No se puede cambiar la pieza de '{Status}' a '{status}'.");

        Status = status;
    }
}
