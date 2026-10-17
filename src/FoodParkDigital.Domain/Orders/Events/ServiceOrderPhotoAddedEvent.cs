using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Orders.Events;

/// <summary>A photo was added to a service order (Fase 25): the activity keeps which one and its file (<c>/files/{key}</c>).</summary>
public sealed record ServiceOrderPhotoAddedEvent(
    Guid OrderId,
    Guid TenantId,
    Guid PhotoId,
    ServicePhotoStage Stage,
    string FileUrl) : DomainEvent;
