using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Orders.Entities;

public class ServiceOrderPiece : AuditableEntity, ITenantScoped
{
    private static readonly Dictionary<ServiceOrderPieceStatus, ServiceOrderPieceStatus[]> AllowedTransitions = new()
    {
        { ServiceOrderPieceStatus.Accepted, [ServiceOrderPieceStatus.InProgress, ServiceOrderPieceStatus.Cancelled] },
        { ServiceOrderPieceStatus.InProgress, [ServiceOrderPieceStatus.Completed, ServiceOrderPieceStatus.Cancelled] },
        { ServiceOrderPieceStatus.Completed, [] },
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
        string? internalNotes,
        Guid? serviceProductId = null,
        bool isTrackable = true,
        bool notifyCustomer = false)
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
        ServiceProductId = serviceProductId;
        IsTrackable = isTrackable;
        NotifyCustomer = notifyCustomer;
        Status = ServiceOrderPieceStatus.Accepted;
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
    public Guid? ServiceProductId { get; private set; }
    public bool IsTrackable { get; private set; }
    public bool NotifyCustomer { get; private set; }
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
