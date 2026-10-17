using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Application.Orders.DTOs;

/// <summary>
/// The photos of a service order for its team (Fase 25, ADR 0024): the ones taken when it was received and the ones
/// added later, before and after. The customer does not receive them.
/// </summary>
public class ServiceOrderPhotosDto
{
    public Guid OrderId { get; set; }
    /// <summary>Whether it still takes photos: every service order but a cancelled one.</summary>
    public bool CanAdd { get; set; }
    /// <summary>Whether it still takes photos before: not once it is delivered or finished, when only the after goes.</summary>
    public bool CanAddBefore { get; set; }
    /// <summary>Those taken when it was received first, then the added ones in the order they were taken.</summary>
    public List<ServiceOrderPhotoDto> Photos { get; set; } = [];
}

public class ServiceOrderPhotoDto
{
    /// <summary>None for a photo taken when the order was received: it is kept with the order, not on its own.</summary>
    public Guid? Id { get; set; }
    public ServicePhotoStage Stage { get; set; }
    /// <summary>The private file (<c>/files/{key}</c>), opened through the API by the business's team.</summary>
    public string Url { get; set; } = null!;
    public string? FileName { get; set; }
    public string? Note { get; set; }
    /// <summary>Taken when the order was received, with it.</summary>
    public bool AtIntake { get; set; }
    /// <summary>When it was added; none for one taken with the order (the order's own time says when).</summary>
    public DateTimeOffset? TakenAt { get; set; }
    public string? TakenBy { get; set; }
    /// <summary>
    /// Whether the person asking may remove it: an added photo, while the order is open, by whoever took it or a
    /// manager of the business.
    /// </summary>
    public bool CanRemove { get; set; }
}
