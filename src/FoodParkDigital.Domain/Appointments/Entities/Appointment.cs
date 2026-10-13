using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.HomeDelivery;

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
        string? notes = null,
        Guid? serviceProductId = null,
        Guid? staffUserId = null)
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
        ServiceProductId = serviceProductId;
        StaffUserId = staffUserId;
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
    /// <summary>The service of the catalog it books (its name is kept in <see cref="ServiceName"/>); null for older ones.</summary>
    public Guid? ServiceProductId { get; private set; }
    /// <summary>The team member who attends it (their name is kept in <see cref="StaffName"/>); null for older ones.</summary>
    public Guid? StaffUserId { get; private set; }

    /// <summary>
    /// Where it happens when it is at the customer's address (Fase 22E); none when the customer comes to the business.
    /// Always loaded with the appointment: the agenda, the travel time and its sale need it.
    /// </summary>
    public AppointmentVisit? Visit { get; private set; }
    /// <summary>The sale it was charged with (Fase 22E); none until it is charged.</summary>
    public Guid? SaleOrderId { get; private set; }
    /// <summary>Changes with every save, so two charges (or a charge and a change) saved at the same time collide.</summary>
    public byte[] Version { get; private set; } = null!;

    public bool IsHomeVisit => Visit is not null;

    public bool IsClosed => Status is AppointmentStatus.Cancelled or AppointmentStatus.Completed or AppointmentStatus.NoShow;

    public void Update(
        string customerName,
        string customerPhone,
        string serviceName,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        string? staffName,
        string? notes,
        Guid? serviceProductId = null,
        Guid? staffUserId = null)
    {
        EnsureOpen();
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
        ServiceProductId = serviceProductId;
        StaffUserId = staffUserId;
    }

    public void ChangeStatus(AppointmentStatus newStatus)
    {
        EnsureOpen();
        Status = newStatus;
    }

    /// <summary>
    /// The appointment happens at the customer's address: the zone must reach its postal code (and be active), and the
    /// visit charges what the zone charges to go there (its minimum and «envío gratis desde» are for orders). At the same
    /// address it keeps the zone and the charge it was arranged with: what the customer agreed to.
    /// </summary>
    /// <returns>False when it was already at that address.</returns>
    public bool ArrangeVisit(DeliveryAddress address, DeliveryZone zone, DateTimeOffset changedAt)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(zone);
        EnsureOpen();
        if (Visit is not null && Visit.IsAt(address))
            return UpdateVisitReferences(address, changedAt);
        if (zone.TenantId != TenantId || !zone.Covers(address.PostalCode))
            throw new DomainException(HomeVisitRules.NotCoveredMessage(address.PostalCode));

        if (Visit is null)
            Visit = new AppointmentVisit(TenantId, Id, address, zone.Name, zone.Fee);
        else
            Visit.MoveTo(address, zone.Name, zone.Fee);
        // The appointment changes with its visit, so its version catches a charge (or a cancellation) saved meanwhile.
        UpdatedAt = changedAt;
        return true;
    }

    /// <summary>
    /// The same place with other references («timbre 6»): the zone and the charge agreed stay, even if the zone no longer
    /// reaches the postal code, and nothing is quoted again.
    /// </summary>
    /// <returns>False when they were the same.</returns>
    public bool UpdateVisitReferences(DeliveryAddress address, DateTimeOffset changedAt)
    {
        ArgumentNullException.ThrowIfNull(address);
        EnsureOpen();
        if (Visit is null || !Visit.IsAt(address))
            throw new InvalidOperationException("Only the references of the place already arranged change without a zone.");
        if (!Visit.ChangeReferences(address.References))
            return false;

        UpdatedAt = changedAt;
        return true;
    }

    /// <summary>The customer comes to the business after all: the visit, and its charge, are dropped.</summary>
    /// <returns>False when it was not a visit.</returns>
    public bool MoveToBusiness(DateTimeOffset changedAt)
    {
        EnsureOpen();
        if (Visit is null)
            return false;

        Visit = null;
        UpdatedAt = changedAt;
        return true;
    }

    /// <summary>
    /// The appointment was charged with <paramref name="orderId"/>: it is done, if it was still open. A cancelled
    /// appointment, or one the customer missed, is not charged; one already charged only again when its sale was cancelled.
    /// </summary>
    public void RecordSale(Guid orderId, bool previousSaleCancelled = false)
    {
        if (orderId == Guid.Empty)
            throw new ArgumentException("The sale is required.", nameof(orderId));
        if (Status == AppointmentStatus.Cancelled)
            throw new DomainException(HomeVisitRules.CancelledSaleMessage);
        if (Status == AppointmentStatus.NoShow)
            throw new DomainException(HomeVisitRules.NoShowSaleMessage);
        if (SaleOrderId is not null && !previousSaleCancelled)
            throw new DomainException(HomeVisitRules.AlreadySoldMessage);

        SaleOrderId = orderId;
        Status = AppointmentStatus.Completed;
    }

    private void EnsureOpen()
    {
        if (IsClosed)
            throw new DomainException("La cita ya está cerrada y no permite más cambios.");
    }
}
