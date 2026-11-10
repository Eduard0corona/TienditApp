using FoodParkDigital.Application.Common.Interfaces;

namespace FoodParkDigital.Application.Tests.Support;

/// <summary>What «Mis pedidos» of each account heard (Fase 31A, ADR 0083): only which of its orders changed.</summary>
public sealed class RecordingCustomerOrders : ICustomerOrderNotificationService
{
    public List<(Guid CustomerUserId, Guid OrderId)> Changed { get; } = [];

    public Task NotifyOrderChanged(Guid customerUserId, Guid orderId)
    {
        Changed.Add((customerUserId, orderId));
        return Task.CompletedTask;
    }
}
