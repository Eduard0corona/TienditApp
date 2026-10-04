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

    /// <summary>
    /// Takes a refund out of the same drawer a payment would enter (cash refunds lower the expected cash;
    /// other methods are recorded apart).
    /// </summary>
    /// <returns>False when no open session could record it; the refund is still recorded on the order.</returns>
    Task<bool> RegisterOrderRefundAsync(Order order, OrderRefund refund, CancellationToken cancellationToken);
}
