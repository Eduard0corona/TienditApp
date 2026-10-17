using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Orders.Entities;

/// <summary>
/// A photo added to a service order after it was received (Fase 25, ADR 0024): before or after, with an optional note,
/// who took it and when. Only for the business's team: the customer does not receive it. The file is private
/// (<c>/files/{key}</c>, Fase 8A). The photos taken when the order was received stay in its detail
/// (<see cref="ServiceOrderDetail.BeforePhotosJson"/>).
/// </summary>
public class ServiceOrderPhoto : BaseEntity, ITenantScoped
{
    public const int MaxPerStage = 20;
    public const int MaxNoteLength = 300;
    public const int MaxFileUrlLength = 500;
    public const int MaxFileNameLength = 255;
    public const int MaxTakenByLength = 256;

    private ServiceOrderPhoto() { }

    internal ServiceOrderPhoto(
        Guid tenantId,
        Guid orderId,
        ServicePhotoStage stage,
        string fileUrl,
        string? fileName,
        string? note,
        DateTimeOffset takenAt,
        string? takenBy,
        Guid? takenByUserId)
    {
        TenantId = tenantId;
        OrderId = orderId;
        Stage = stage;
        FileUrl = fileUrl;
        FileName = fileName;
        Note = note;
        TakenAt = takenAt;
        TakenBy = takenBy;
        TakenByUserId = takenByUserId;
    }

    public Guid TenantId { get; private set; }
    public Guid OrderId { get; private set; }
    public ServicePhotoStage Stage { get; private set; }
    /// <summary>The private file (<c>/files/{key}</c>), checked as the business's own by whoever adds it.</summary>
    public string FileUrl { get; private set; } = null!;
    public string? FileName { get; private set; }
    /// <summary>What the photo shows («Mancha en la manga»); none when nobody wrote it.</summary>
    public string? Note { get; private set; }
    public DateTimeOffset TakenAt { get; private set; }
    /// <summary>Who added it, by the name of the session.</summary>
    public string? TakenBy { get; private set; }
    /// <summary>The account that added it: while the order is open, it may remove the photo (and so may a manager).</summary>
    public Guid? TakenByUserId { get; private set; }
}
