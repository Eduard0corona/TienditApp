namespace FoodParkDigital.Domain.Appointments.HomeVisits;

/// <summary>
/// What a visit to quote costs the customer (Fase 29A, ADR 0053 point 10), written like the prices of the business (with
/// or without IVA, as its other prices): nothing «Sin costo»; otherwise the price of the service the business picked as
/// its visit, when it has one, plus what the zone that reaches the customer's postal code charges for going there. «Se
/// descuenta si contratas» costs the same as «Se cobra»: the quote takes it off later (ADR 0053, stage 3).
/// </summary>
public static class HomeVisitCharge
{
    /// <summary>The visit to a postal code that a zone with <paramref name="zoneFee"/> reaches.</summary>
    /// <param name="servicePrice">The price of the visit service; null without one, or when it is not offered now.</param>
    public static decimal To(HomeVisitChargePolicy policy, decimal? servicePrice, decimal zoneFee)
        => policy == HomeVisitChargePolicy.Free ? 0m : (servicePrice ?? 0m) + zoneFee;

    /// <summary>
    /// Before the customer says where: the visit to the zone that charges least, and whether it is a starting price
    /// («desde»: some zone charges more). Null for a business without active zones, which goes nowhere.
    /// </summary>
    /// <param name="zoneFees">What each active zone of the business charges.</param>
    public static HomeVisitPrice? Starting(HomeVisitChargePolicy policy, decimal? servicePrice, IReadOnlyCollection<decimal> zoneFees)
    {
        if (zoneFees.Count == 0)
            return null;
        if (policy == HomeVisitChargePolicy.Free)
            return new HomeVisitPrice(0m, IsStarting: false);

        var cheapest = zoneFees.Min();
        return new HomeVisitPrice(To(policy, servicePrice, cheapest), IsStarting: zoneFees.Any(fee => fee != cheapest));
    }
}

/// <summary>What a visit costs, and whether it is the least it costs («desde»), because it depends on the postal code.</summary>
public readonly record struct HomeVisitPrice(decimal Amount, bool IsStarting);
