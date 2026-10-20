using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Application.Common.Interfaces;

public interface IOrderCashService
{
    /// <summary>
    /// Adds the payment to the drawer that takes it: the current user's open session, else the only open
    /// session of the sale's branch (see <c>OrderCashService.PickDrawer</c>).
    /// </summary>
    /// <returns>False when no open session could take it; the caller refuses the payment.</returns>
    Task<bool> RegisterOrderPaymentAsync(Order order, OrderPayment payment, CancellationToken cancellationToken);

    /// <summary>
    /// Takes a refund out of the same drawer a payment would enter (cash refunds lower the expected cash;
    /// other methods are recorded apart).
    /// </summary>
    /// <returns>False when no open session could record it; the caller refuses the refund.</returns>
    Task<bool> RegisterOrderRefundAsync(Order order, OrderRefund refund, CancellationToken cancellationToken);

    /// <summary>
    /// What a customer leaves in their favor (<see cref="CashTransactionType.CustomerDeposit"/>) or gets back of it
    /// (<see cref="CashTransactionType.CustomerDepositReturn"/>), Fase 26D: a movement of the drawer of whoever charges,
    /// their own open session, else the only open one of the business, as a sale without branch.
    /// </summary>
    /// <returns>The drawer that took it; null when no open session could, and the caller refuses it.</returns>
    Task<Guid?> RegisterCustomerBalanceAsync(
        Guid tenantId,
        CashTransactionType transactionType,
        decimal amount,
        OrderPaymentMethod method,
        DateTimeOffset occurredAt,
        string? reference,
        string? notes,
        CancellationToken cancellationToken);
}
