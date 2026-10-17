using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Orders.Events;

/// <summary>
/// A photo was removed from an open service order (Fase 25): the activity keeps which one, its file, who took it and when,
/// since the row is gone.
/// </summary>
public sealed record ServiceOrderPhotoRemovedEvent(
    Guid OrderId,
    Guid TenantId,
    Guid PhotoId,
    ServicePhotoStage Stage,
    string FileUrl,
    DateTimeOffset TakenAt,
    string? TakenBy) : DomainEvent;
