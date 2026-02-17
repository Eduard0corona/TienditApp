using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Orders.Entities;

namespace FoodParkDigital.Application.Orders;

internal sealed class NoOpOrderInventoryService : IOrderInventoryService
{
    public static IOrderInventoryService Instance { get; } = new NoOpOrderInventoryService();

    private NoOpOrderInventoryService() { }

    public Task ApplyConsumptionForOrderAsync(Order order, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ReverseConsumptionForOrderAsync(Order order, string? reason, CancellationToken cancellationToken) => Task.CompletedTask;
}
