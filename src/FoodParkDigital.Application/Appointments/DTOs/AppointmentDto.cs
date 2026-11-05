using System.Linq.Expressions;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Appointments.OnlineBooking;

namespace FoodParkDigital.Application.Appointments.DTOs;

public class AppointmentDto
{
    public Guid Id { get; set; }
    public string CustomerName { get; set; } = null!;
    public string CustomerPhone { get; set; } = null!;
    /// <summary>Where the customer of a visit requested online gets its quote (Fase 29B); null when they gave none.</summary>
    public string? CustomerEmail { get; set; }
    public string ServiceName { get; set; } = null!;
    public string? StaffName { get; set; }
    public Guid? ServiceProductId { get; set; }
    public Guid? StaffUserId { get; set; }
    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset EndAt { get; set; }
    public AppointmentStatus Status { get; set; }
    public string? Notes { get; set; }
    /// <summary>Where it happens when it is at the customer's address (Fase 22E); null when they come to the business.</summary>
    public AppointmentVisitDto? Visit { get; set; }
    /// <summary>The sale it was charged with (Fase 22E); null until it is charged.</summary>
    public Guid? SaleOrderId { get; set; }
    /// <summary>Its sale was cancelled: it is charged again with another one.</summary>
    public bool SaleCancelled { get; set; }
    /// <summary>Who booked it (Fase 24D): the team, or the customer online.</summary>
    public AppointmentChannel Channel { get; set; }
    /// <summary>Its customer cancelled it from the account or the link (Fase 24D).</summary>
    public bool CancelledByCustomer { get; set; }
    /// <summary>
    /// The service order whose job it does (Fase 29F): charged in that order, so it is never charged on its own nor quoted.
    /// Null for every other appointment.
    /// </summary>
    public Guid? WorkOrderId { get; set; }
    /// <summary>The folio of that order, for the agenda; null for every other appointment.</summary>
    public string? WorkOrderNumber { get; set; }
    /// <summary>
    /// The photos of what happens the customer sent with a visit requested online (Fase 29H, ADR 0062), as the private paths
    /// that open them (<c>/files/…</c>), in the order sent. The agenda's queries read them; the answer of a command leaves
    /// them empty.
    /// </summary>
    public List<string> ProblemPhotos { get; set; } = [];
    /// <summary>
    /// By when a request online still waiting is answered in time (Fase 29L, ADR 0066): the time its customer asked for, or a
    /// day after it arrived, whichever comes first (<see cref="ResponseTimes.AnswerBy"/>). Null for every other appointment,
    /// one from the business's own team too: it does not count (Fase 29M).
    /// </summary>
    public DateTimeOffset? AnswerBy { get; set; }
    /// <summary>
    /// A request online from the business's own team (Fase 29M, ADR 0067): Explorar does not count how soon it is answered.
    /// </summary>
    public bool FromTeam { get; set; }

    public static AppointmentDto From(Appointment appointment) => new()
    {
        Id = appointment.Id,
        CustomerName = appointment.CustomerName,
        CustomerPhone = appointment.CustomerPhone,
        CustomerEmail = appointment.CustomerEmail,
        ServiceName = appointment.ServiceName,
        StaffName = appointment.StaffName,
        ServiceProductId = appointment.ServiceProductId,
        StaffUserId = appointment.StaffUserId,
        StartAt = appointment.StartAt,
        EndAt = appointment.EndAt,
        Status = appointment.Status,
        Notes = appointment.Notes,
        Visit = appointment.Visit is null ? null : new AppointmentVisitDto
        {
            PostalCode = appointment.Visit.PostalCode,
            AddressLine = appointment.Visit.AddressLine,
            References = appointment.Visit.References,
            ZoneName = appointment.Visit.ZoneName,
            Fee = appointment.Visit.Fee,
            ChargePolicy = appointment.Visit.ChargePolicy
        },
        SaleOrderId = appointment.SaleOrderId,
        Channel = appointment.Channel,
        CancelledByCustomer = appointment.CancelledByCustomerAt != null,
        WorkOrderId = appointment.WorkOrderId,
        AnswerBy = appointment.Status == AppointmentStatus.Requested && !appointment.FromTeam
            ? ResponseTimes.AnswerBy(appointment.CreatedAt, appointment.RequestedStartAt ?? appointment.StartAt)
            : null,
        FromTeam = appointment.FromTeam
    };

    /// <summary><see cref="From"/> written for SQL, for the lists.</summary>
    public static readonly Expression<Func<Appointment, AppointmentDto>> Projection = x => new AppointmentDto
    {
        Id = x.Id,
        CustomerName = x.CustomerName,
        CustomerPhone = x.CustomerPhone,
        CustomerEmail = x.CustomerEmail,
        ServiceName = x.ServiceName,
        StaffName = x.StaffName,
        ServiceProductId = x.ServiceProductId,
        StaffUserId = x.StaffUserId,
        StartAt = x.StartAt,
        EndAt = x.EndAt,
        Status = x.Status,
        Notes = x.Notes,
        Visit = x.Visit == null ? null : new AppointmentVisitDto
        {
            PostalCode = x.Visit.PostalCode,
            AddressLine = x.Visit.AddressLine,
            References = x.Visit.References,
            ZoneName = x.Visit.ZoneName,
            Fee = x.Visit.Fee,
            ChargePolicy = x.Visit.ChargePolicy
        },
        SaleOrderId = x.SaleOrderId,
        Channel = x.Channel,
        CancelledByCustomer = x.CancelledByCustomerAt != null,
        WorkOrderId = x.WorkOrderId,
        // ResponseTimes.AnswerBy, written for SQL.
        AnswerBy = x.Status == AppointmentStatus.Requested && !x.FromTeam
            ? ((x.RequestedStartAt ?? x.StartAt) < x.CreatedAt.AddHours(AnswerByHours)
                ? x.RequestedStartAt ?? x.StartAt
                : x.CreatedAt.AddHours(AnswerByHours))
            : null,
        FromTeam = x.FromTeam
    };

    /// <summary>A day: the longest a request waits and is still answered in time (<see cref="ResponseTimes.AnswerBy"/>).</summary>
    private static readonly int AnswerByHours = ResponseTimes.LimitHours[^1];
}

/// <summary>
/// The customer's address of an appointment at their home (Fase 22E), the zone that reaches it and the charge for the visit
/// agreed then (with the business's tax policy, like its prices; 0 when the zone charges nothing).
/// </summary>
public class AppointmentVisitDto
{
    public string PostalCode { get; set; } = null!;
    public string AddressLine { get; set; } = null!;
    public string? References { get; set; }
    public string ZoneName { get; set; } = null!;
    public decimal Fee { get; set; }
    /// <summary>
    /// A visit to quote the customer requested online (Fase 29B): how the business charges it, as agreed. «Sin costo» keeps
    /// <see cref="Fee"/> at 0 wherever it moves. Null for a visit the team arranged.
    /// </summary>
    public HomeVisitChargePolicy? ChargePolicy { get; set; }
}
