using FoodParkDigital.Domain.Orders.Entities;

namespace FoodParkDigital.Application.Common.Interfaces;

public interface IOrderCashService
{
    /// <summary>
    /// Adds the payment to the drawer that takes it: the current user's open session, else the only open
    /// session of the payment's branch, else the only open session of the business.
    /// </summary>
    /// <returns>False when no open session could take it; the payment is still recorded on the order.</returns>
    Task<bool> RegisterOrderPaymentAsync(Order order, OrderPayment payment, CancellationToken cancellationToken);
}
