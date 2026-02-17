using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Appointments.Entities;

public class Appointment : AuditableEntity, ITenantScoped, IAggregateRoot
{
    private Appointment() { }

    public Appointment(
        Guid tenantId,
        string customerName,
        string customerPhone,
        string serviceName,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        string? staffName = null,
        string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(customerName))
            throw new DomainException("El nombre del cliente es obligatorio.");
        if (string.IsNullOrWhiteSpace(customerPhone))
            throw new DomainException("El teléfono del cliente es obligatorio.");
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new DomainException("El servicio es obligatorio.");
        if (endAt <= startAt)
            throw new DomainException("La fecha de fin debe ser mayor a la fecha de inicio.");

        TenantId = tenantId;
        CustomerName = customerName.Trim();
        CustomerPhone = customerPhone.Trim();
        ServiceName = serviceName.Trim();
        StartAt = startAt;
        EndAt = endAt;
        StaffName = staffName?.Trim();
        Notes = notes?.Trim();
        Status = AppointmentStatus.Scheduled;
    }

    public Guid TenantId { get; private set; }
    public string CustomerName { get; private set; } = null!;
    public string CustomerPhone { get; private set; } = null!;
    public string ServiceName { get; private set; } = null!;
    public string? StaffName { get; private set; }
    public DateTimeOffset StartAt { get; private set; }
    public DateTimeOffset EndAt { get; private set; }
    public AppointmentStatus Status { get; private set; }
    public string? Notes { get; private set; }

    public void Update(
        string customerName,
        string customerPhone,
        string serviceName,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        string? staffName,
        string? notes)
    {
        if (string.IsNullOrWhiteSpace(customerName))
            throw new DomainException("El nombre del cliente es obligatorio.");
        if (string.IsNullOrWhiteSpace(customerPhone))
            throw new DomainException("El teléfono del cliente es obligatorio.");
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new DomainException("El servicio es obligatorio.");
        if (endAt <= startAt)
            throw new DomainException("La fecha de fin debe ser mayor a la fecha de inicio.");

        CustomerName = customerName.Trim();
        CustomerPhone = customerPhone.Trim();
        ServiceName = serviceName.Trim();
        StartAt = startAt;
        EndAt = endAt;
        StaffName = staffName?.Trim();
        Notes = notes?.Trim();
    }

    public void ChangeStatus(AppointmentStatus newStatus)
    {
        if (Status is AppointmentStatus.Cancelled or AppointmentStatus.Completed or AppointmentStatus.NoShow)
            throw new DomainException("La cita ya está cerrada y no permite más cambios.");

        Status = newStatus;
    }
}
