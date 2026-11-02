using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Appointments.Entities;

/// <summary>
/// A photo of what happens that the customer sent with a visit to quote requested online (Fase 29H, ADR 0062), from the link
/// of the request: only for the business, which sees it in the agenda and in the quote of that visit. The file is private
/// (<c>/files/{key}</c>, Fase 8A), saved again as WebP without its metadata. Added through
/// <see cref="Appointment.AddProblemPhoto"/>; it never changes the appointment itself, so sending it never conflicts with
/// the business deciding the request.
/// </summary>
public class AppointmentProblemPhoto : BaseEntity, ITenantScoped
{
    /// <summary>The longest storage key a photo keeps.</summary>
    public const int MaxFileKeyLength = 300;

    private AppointmentProblemPhoto() { }

    internal AppointmentProblemPhoto(Guid tenantId, Guid appointmentId, int position, string fileKey, DateTimeOffset addedAt)
    {
        TenantId = tenantId;
        AppointmentId = appointmentId;
        Position = position;
        FileKey = fileKey;
        AddedAt = addedAt;
    }

    public Guid TenantId { get; private set; }
    public Guid AppointmentId { get; private set; }

    /// <summary>
    /// Its place among the photos of the visit, from 0: the base keeps each place once per appointment and none past the
    /// last, so two photos sent at once never make one more than <see cref="HomeVisits.HomeVisitRules.MaxProblemPhotos"/>.
    /// </summary>
    public int Position { get; private set; }

    /// <summary>The key of the private file (<c>orders/{tenantId}/visit-problem/{yyyyMM}/{guid}.webp</c>).</summary>
    public string FileKey { get; private set; } = null!;

    public DateTimeOffset AddedAt { get; private set; }
}
