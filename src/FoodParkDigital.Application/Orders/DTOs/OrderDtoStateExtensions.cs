using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Orders.Taxes;

namespace FoodParkDigital.Application.Orders.DTOs;

public static class OrderDtoStateExtensions
{
    /// <summary>
    /// Fills what the DTO derives from the order instead of storing it: the allowed actions and the IVA
    /// breakdown (lists are projected in SQL, where the domain rules cannot run).
    /// </summary>
    public static void ApplyStateMetadata(this OrderDto order)
    {
        var stateMachine = OrderStateMachineFactory.Create(order.BusinessType, order.OrderType);
        // An open tab changes state only through its own actions (rounds, close): marking it delivered by
        // hand would free the table with the bill unpaid.
        order.AllowedTransitions = order.IsOpenTab
            ? []
            : stateMachine.GetAllowedTransitions(order.Status).ToList();
        // Mirrors Order.Cancel: money still kept is refunded first (void does both in one step).
        order.CanCancel = stateMachine.CanCancel(order.Status) && order.NetPaidAmount <= 0;

        // Same calculation as Order.GetTaxBreakdown, from what each item charges and the rate stored on it, and the
        // delivery as one more concept.
        var amounts = order.Items
            .Where(item => item.CancelledQuantity < item.Quantity)
            .Select(item => new TaxableAmount(item.ChargedPrice, new VatRate(item.TaxRate, item.IsTaxExempt)));
        if (order.Delivery is { Fee: > 0 } delivery)
            amounts = amounts.Append(new TaxableAmount(delivery.Fee, VatRate.Of(delivery.FeeTaxRate)));
        order.TaxBreakdown = OrderTaxCalculator
            .Calculate(order.PricesIncludeTax, amounts)
            .Select(line => new OrderTaxLineDto
            {
                RatePercent = line.RatePercent,
                IsExempt = line.IsExempt,
                TaxableBase = line.TaxableBase,
                TaxAmount = line.TaxAmount,
                Total = line.Total
            })
            .ToList();
    }

    public static void ApplyStateMetadata(this IEnumerable<OrderDto> orders)
    {
        foreach (var order in orders)
            order.ApplyStateMetadata();
    }
}
