using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Domain.Orders.Entities;

namespace FoodParkDigital.Application.Common.Interfaces;

public interface IOrderInventoryService
{
    /// <summary>
    /// Takes the recipe supplies of the order out of stock once it reaches a consuming status. When they are short
    /// it throws a <see cref="Domain.Exceptions.DomainException"/> naming them, unless the business warns instead
    /// or <paramref name="shortageHandling"/> allows it.
    /// </summary>
    /// <returns>The supplies that went below zero (empty when there was enough or nothing to consume).</returns>
    Task<IReadOnlyList<StockShortage>> ApplyConsumptionForOrderAsync(
        Order order,
        CancellationToken cancellationToken,
        StockShortageHandling shortageHandling = StockShortageHandling.TenantPolicy);

    /// <summary>
    /// The recipe supplies of one round of an open tab when it goes to the kitchen, with the same stock policy as a
    /// sale. Each round leaves the stock once; closing the tab takes only the rounds that did not.
    /// </summary>
    /// <returns>The supplies that went below zero (empty when there was enough or nothing to consume).</returns>
    Task<IReadOnlyList<StockShortage>> ApplyConsumptionForTabRoundAsync(
        Order order,
        int round,
        CancellationToken cancellationToken,
        StockShortageHandling shortageHandling = StockShortageHandling.TenantPolicy);

    /// <summary>The recipe supplies of a service piece once it is completed, with the same stock policy.</summary>
    Task<IReadOnlyList<StockShortage>> ApplyConsumptionForServicePieceAsync(
        Order order,
        ServiceOrderPiece piece,
        CancellationToken cancellationToken,
        StockShortageHandling shortageHandling = StockShortageHandling.TenantPolicy);

    Task ReverseConsumptionForOrderAsync(Order order, string? reason, CancellationToken cancellationToken);

    /// <summary>
    /// Re-enters the recipe supplies of the units returned with <paramref name="refund"/>, when the order
    /// consumed inventory. Idempotent per refund.
    /// </summary>
    Task ReturnItemsToInventoryAsync(Order order, OrderRefund refund, CancellationToken cancellationToken);

    /// <summary>
    /// Re-enters the recipe supplies of a dish cancelled from an open tab, when its round took them and the
    /// cancellation returns them to stock (a dish already prepared is lost instead). Idempotent per cancellation.
    /// </summary>
    Task ReturnCancelledTabItemAsync(Order order, OrderItemCancellation cancellation, CancellationToken cancellationToken);
}
