namespace FoodParkDigital.Domain.Appointments.HomeVisits;

/// <summary>
/// How a business that receives visits to quote charges the visit (ADR 0053): the database keeps the number. The cost is
/// its visit service plus what the zone charges to go there, nothing, or the same and the accepted quote takes it off.
/// </summary>
public enum HomeVisitChargePolicy
{
    /// <summary>«Se cobra»: the visit service plus the charge of the zone.</summary>
    Charged = 0,

    /// <summary>«Sin costo»: neither the service nor the charge of the zone.</summary>
    Free = 1,

    /// <summary>«Se descuenta si contratas»: charged as the first, and the accepted quote takes off what was paid.</summary>
    CreditedIfHired = 2
}
