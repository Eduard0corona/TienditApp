using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Orders.Entities;

public class ServiceOrderDetail : AuditableEntity, ITenantScoped
{
    private ServiceOrderDetail() { }

    public ServiceOrderDetail(
        Guid tenantId,
        Guid orderId,
        DateTimeOffset receivedAt,
        DateTimeOffset? promisedAt,
        int priority,
        string? intakeNotes,
        string? deliveryNotes,
        string? intakeChecklistJson,
        string? beforePhotosJson,
        string? afterPhotosJson)
    {
        TenantId = tenantId;
        OrderId = orderId;
        ReceivedAt = receivedAt;
        PromisedAt = promisedAt;
        Priority = priority;
        IntakeNotes = intakeNotes;
        DeliveryNotes = deliveryNotes;
        IntakeChecklistJson = intakeChecklistJson;
        BeforePhotosJson = beforePhotosJson;
        AfterPhotosJson = afterPhotosJson;
    }

    public Guid TenantId { get; private set; }
    public Guid OrderId { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public DateTimeOffset? PromisedAt { get; private set; }
    public int Priority { get; private set; }
    public string? IntakeNotes { get; private set; }
    public string? DeliveryNotes { get; private set; }
    public string? IntakeChecklistJson { get; private set; }
    public string? BeforePhotosJson { get; private set; }
    public string? AfterPhotosJson { get; private set; }

    public Order Order { get; private set; } = null!;

    public void Update(
        DateTimeOffset? promisedAt,
        int priority,
        string? intakeNotes,
        string? deliveryNotes,
        string? intakeChecklistJson,
        string? beforePhotosJson,
        string? afterPhotosJson)
    {
        PromisedAt = promisedAt;
        Priority = priority;
        IntakeNotes = intakeNotes;
        DeliveryNotes = deliveryNotes;
        IntakeChecklistJson = intakeChecklistJson;
        BeforePhotosJson = beforePhotosJson;
        AfterPhotosJson = afterPhotosJson;
    }
}
