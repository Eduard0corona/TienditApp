using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Orders.HomeDelivery;

namespace FoodParkDigital.Domain.Appointments.Entities;

/// <summary>
/// Where an appointment happens when it is at the customer's address (Fase 22E), arranged with the appointment
/// (<see cref="Appointment.ArrangeVisit"/>): the address, a copy of the zone's name and the charge for the visit agreed then.
/// The charge is fixed while the address stays the same, even if the zone changes later: it is what the customer agreed to.
/// It has no IVA rate: the rate is the one of the business when the appointment is charged, like the delivery of an order.
/// </summary>
public class AppointmentVisit : BaseEntity, ITenantScoped
{
    private AppointmentVisit() { }

    internal AppointmentVisit(Guid tenantId, Guid appointmentId, DeliveryAddress address, string zoneName, decimal fee)
    {
        TenantId = tenantId;
        AppointmentId = appointmentId;
        MoveTo(address, zoneName, fee);
    }

    public Guid TenantId { get; private set; }
    public Guid AppointmentId { get; private set; }

    public string PostalCode { get; private set; } = null!;
    public string AddressLine { get; private set; } = null!;
    public string? References { get; private set; }

    /// <summary>The zone the postal code was in when the visit was arranged, for the team.</summary>
    public string ZoneName { get; private set; } = null!;
    /// <summary>What going there charges, with the tax policy of the sale like any price of it; 0 when it is free.</summary>
    public decimal Fee { get; private set; }

    /// <summary>Whether it is already at <paramref name="address"/>, as written.</summary>
    internal bool IsAt(DeliveryAddress address)
        => PostalCode == address.PostalCode && AddressLine == address.AddressLine && References == address.References;

    internal void MoveTo(DeliveryAddress address, string zoneName, decimal fee)
    {
        PostalCode = address.PostalCode;
        AddressLine = address.AddressLine;
        References = address.References;
        ZoneName = zoneName;
        Fee = fee;
    }
}
