using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Orders.HomeDelivery;

namespace FoodParkDigital.Domain.Appointments.Entities;

/// <summary>
/// Where an appointment happens when it is at the customer's address (Fase 22E), arranged with the appointment
/// (<see cref="Appointment.ArrangeVisit"/>): the address, a copy of the zone's name and the charge for the visit agreed then.
/// The charge is fixed while the address stays the same, even if the zone changes later: it is what the customer agreed to.
/// It has no IVA rate: the rate is the one of the business when the appointment is charged, like the delivery of an order.
/// A visit to quote a customer requested online (Fase 29B, ADR 0053) also keeps how the business charged it then.
/// </summary>
public class AppointmentVisit : BaseEntity, ITenantScoped
{
    private AppointmentVisit() { }

    /// <param name="zoneFee">What the zone charges to go there.</param>
    /// <param name="chargePolicy">How a visit to quote requested online is charged; null for one the team arranged.</param>
    internal AppointmentVisit(
        Guid tenantId, Guid appointmentId, DeliveryAddress address, string zoneName, decimal zoneFee, HomeVisitChargePolicy? chargePolicy = null)
    {
        if (chargePolicy is { } policy && !Enum.IsDefined(policy))
            throw new ArgumentOutOfRangeException(nameof(chargePolicy));

        TenantId = tenantId;
        AppointmentId = appointmentId;
        ChargePolicy = chargePolicy;
        MoveTo(address, zoneName, zoneFee);
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

    /// <summary>
    /// How the business charges this visit to quote, as it said when the customer requested it online (Fase 29B, ADR 0053
    /// point 12): what the customer agreed to, even if the business changes it later. «Sin costo» keeps <see cref="Fee"/>
    /// at 0 wherever the visit moves. Null for a visit the team arranged in the agenda (Fase 22E).
    /// </summary>
    public HomeVisitChargePolicy? ChargePolicy { get; private set; }

    /// <summary>
    /// Whether it is already at the place of <paramref name="address"/> (its postal code and its address, as written): then it
    /// keeps what was agreed, even if its zone no longer reaches it. The references only say how to find it.
    /// </summary>
    public bool IsAt(DeliveryAddress address)
        => PostalCode == address.PostalCode && AddressLine == address.AddressLine;

    /// <returns>False when they were the same.</returns>
    internal bool ChangeReferences(string? references)
    {
        if (References == references)
            return false;

        References = references;
        return true;
    }

    /// <param name="zoneFee">What the zone that reaches it charges to go there.</param>
    internal void MoveTo(DeliveryAddress address, string zoneName, decimal zoneFee)
    {
        PostalCode = address.PostalCode;
        AddressLine = address.AddressLine;
        References = address.References;
        ZoneName = zoneName;
        Fee = ChargePolicy is { } policy ? HomeVisitCharge.ZoneFee(policy, zoneFee) : zoneFee;
    }
}
