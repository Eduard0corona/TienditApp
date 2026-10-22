namespace FoodParkDigital.Domain.Orders.Platforms;

/// <summary>What a delivery platform owes the business for one order, as a settlement counts it.</summary>
public static class PlatformSettlementCalculator
{
    /// <summary>
    /// The order's expected net in proportion to what is still paid of it, rounded once to cents (banker's rounding, as
    /// <see cref="PlatformOrderCalculator"/>): the share Ventas por canal gives the order. Charged by the platform, it is
    /// the total minus what was given back; charged by the business, only what it collected, negative (the commission the
    /// platform deducts). Zero once everything is given back, or while the business has not collected it.
    /// </summary>
    /// <param name="expectedSettlement">What the order copied when it was registered (<see cref="Entities.PlatformOrderDetail.ExpectedSettlement"/>).</param>
    /// <param name="netPaidAmount">What is paid of the order minus what was given back (<see cref="Entities.Order.NetPaidAmount"/>).</param>
    /// <param name="orderTotal">The order's total when it was registered: always more than zero.</param>
    public static decimal ExpectedNet(decimal expectedSettlement, decimal netPaidAmount, decimal orderTotal)
        => InProportion(expectedSettlement, netPaidAmount, orderTotal);

    /// <summary>
    /// Any amount the order copied when it was registered (its expected net, its ISR or IVA withheld, its subtotal) in
    /// proportion to what is still paid of it, rounded once to cents with banker's rounding: a refund of half the order
    /// halves its withholdings too (Fase 22J).
    /// </summary>
    /// <param name="orderTotal">The order's total when it was registered: always more than zero.</param>
    public static decimal InProportion(decimal amount, decimal netPaidAmount, decimal orderTotal)
    {
        if (orderTotal <= 0)
            throw new ArgumentOutOfRangeException(nameof(orderTotal), orderTotal, "An order of a delivery platform always has a total.");

        // Paid in full, the product and the division give back the copied amount exactly.
        return decimal.Round(amount * netPaidAmount / orderTotal, 2, MidpointRounding.ToEven);
    }
}
