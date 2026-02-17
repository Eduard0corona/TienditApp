using FoodParkDigital.Domain.Appointments.Enums;

namespace FoodParkDigital.Application.Appointments.DTOs;

public class AppointmentDto
{
    public Guid Id { get; set; }
    public string CustomerName { get; set; } = null!;
    public string CustomerPhone { get; set; } = null!;
    public string ServiceName { get; set; } = null!;
    public string? StaffName { get; set; }
    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset EndAt { get; set; }
    public AppointmentStatus Status { get; set; }
    public string? Notes { get; set; }
}
