using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Domain.Orders.Entities;

namespace FoodParkDigital.Application.Orders;

internal sealed class NoOpOrderInventoryService : IOrderInventoryService
{
    public static IOrderInventoryService Instance { get; } = new NoOpOrderInventoryService();

    private NoOpOrderInventoryService() { }

    public Task<IReadOnlyList<StockShortage>> ApplyConsumptionForOrderAsync(
        Order order,
        CancellationToken cancellationToken,
        StockShortageHandling shortageHandling = StockShortageHandling.TenantPolicy) => Task.FromResult(NoShortages);

    public Task<IReadOnlyList<StockShortage>> ApplyConsumptionForServicePieceAsync(
        Order order,
        ServiceOrderPiece piece,
        CancellationToken cancellationToken,
        StockShortageHandling shortageHandling = StockShortageHandling.TenantPolicy) => Task.FromResult(NoShortages);

    private static readonly IReadOnlyList<StockShortage> NoShortages = [];

    public Task ReverseConsumptionForOrderAsync(Order order, string? reason, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ReturnItemsToInventoryAsync(Order order, OrderRefund refund, CancellationToken cancellationToken) => Task.CompletedTask;
}
