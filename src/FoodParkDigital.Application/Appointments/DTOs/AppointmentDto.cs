using System.Linq.Expressions;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.Enums;

namespace FoodParkDigital.Application.Appointments.DTOs;

public class AppointmentDto
{
    public Guid Id { get; set; }
    public string CustomerName { get; set; } = null!;
    public string CustomerPhone { get; set; } = null!;
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

    public static AppointmentDto From(Appointment appointment) => new()
    {
        Id = appointment.Id,
        CustomerName = appointment.CustomerName,
        CustomerPhone = appointment.CustomerPhone,
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
            Fee = appointment.Visit.Fee
        },
        SaleOrderId = appointment.SaleOrderId
    };

    /// <summary><see cref="From"/> written for SQL, for the lists.</summary>
    public static readonly Expression<Func<Appointment, AppointmentDto>> Projection = x => new AppointmentDto
    {
        Id = x.Id,
        CustomerName = x.CustomerName,
        CustomerPhone = x.CustomerPhone,
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
            Fee = x.Visit.Fee
        },
        SaleOrderId = x.SaleOrderId
    };
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
}
