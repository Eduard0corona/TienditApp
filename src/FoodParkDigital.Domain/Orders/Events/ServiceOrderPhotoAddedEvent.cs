using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Orders.Events;

/// <summary>A photo was added to a service order (Fase 25): it goes to the order's activity.</summary>
public sealed record ServiceOrderPhotoAddedEvent(
    Guid OrderId,
    Guid TenantId,
    Guid PhotoId,
    ServicePhotoStage Stage,
    string? FileName) : DomainEvent;
