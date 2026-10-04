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

    public static AppointmentDto From(Domain.Appointments.Entities.Appointment appointment) => new()
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
        Notes = appointment.Notes
    };
}
