using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Orders.Entities;

namespace FoodParkDigital.Application.Orders;

internal sealed class NoOpOrderCashService : IOrderCashService
{
    public static readonly NoOpOrderCashService Instance = new();

    private NoOpOrderCashService() { }

    public Task<bool> RegisterOrderPaymentAsync(Order order, OrderPayment payment, CancellationToken cancellationToken)
        => Task.FromResult(false);
}
