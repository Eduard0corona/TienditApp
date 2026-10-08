using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Domain.Orders.Entities;

namespace FoodParkDigital.Application.Tests.Support;

/// <summary>
/// For tests that do not exercise supplies: consumes nothing. Production handlers require the real service (they no
/// longer fall back to one like this without notice).
/// </summary>
internal sealed class NoOpOrderInventoryService : IOrderInventoryService
{
    public static IOrderInventoryService Instance { get; } = new NoOpOrderInventoryService();

    private static readonly IReadOnlyList<StockShortage> NoShortages = [];

    private NoOpOrderInventoryService() { }

    public Task<IReadOnlyList<StockShortage>> ApplyConsumptionForOrderAsync(
        Order order,
        CancellationToken cancellationToken,
        StockShortageHandling shortageHandling = StockShortageHandling.TenantPolicy) => Task.FromResult(NoShortages);

    public Task<IReadOnlyList<StockShortage>> ApplyConsumptionForTabRoundAsync(
        Order order,
        int round,
        CancellationToken cancellationToken,
        StockShortageHandling shortageHandling = StockShortageHandling.TenantPolicy) => Task.FromResult(NoShortages);

    public Task<IReadOnlyList<StockShortage>> ApplyConsumptionForServicePieceAsync(
        Order order,
        ServiceOrderPiece piece,
        CancellationToken cancellationToken,
        StockShortageHandling shortageHandling = StockShortageHandling.TenantPolicy) => Task.FromResult(NoShortages);

    public Task ReverseConsumptionForOrderAsync(Order order, string? reason, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ReturnItemsToInventoryAsync(Order order, OrderRefund refund, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ReturnCancelledTabItemAsync(Order order, OrderItemCancellation cancellation, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

/// <summary>
/// For tests that do not exercise the cash drawer: as if an open drawer took every payment and refund (they are
/// refused without one), without recording a cash movement.
/// </summary>
internal sealed class NoOpOrderCashService : IOrderCashService
{
    public static readonly NoOpOrderCashService Instance = new();

    private NoOpOrderCashService() { }

    public Task<bool> RegisterOrderPaymentAsync(Order order, OrderPayment payment, CancellationToken cancellationToken)
        => Task.FromResult(true);

    public Task<bool> RegisterOrderRefundAsync(Order order, OrderRefund refund, CancellationToken cancellationToken)
        => Task.FromResult(true);
}
