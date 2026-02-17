using FoodParkDigital.Domain.Orders.Entities;

namespace FoodParkDigital.Application.Common.Interfaces;

public interface IOrderCashService
{
    Task RegisterOrderPaymentAsync(Order order, OrderPayment payment, CancellationToken cancellationToken);
}
