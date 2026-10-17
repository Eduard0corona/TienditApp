using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Orders.Events;

/// <summary>A photo was removed from an open service order (Fase 25): the activity keeps which one, who took it and when.</summary>
public sealed record ServiceOrderPhotoRemovedEvent(
    Guid OrderId,
    Guid TenantId,
    Guid PhotoId,
    ServicePhotoStage Stage,
    string? FileName,
    DateTimeOffset TakenAt,
    string? TakenBy) : DomainEvent;
