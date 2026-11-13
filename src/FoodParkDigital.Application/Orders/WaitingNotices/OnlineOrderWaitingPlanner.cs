using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders.WaitingNotices;

/// <summary>
/// Reminds a business by email of the orders of its online menu that still wait to be accepted 15 minutes after they arrived
/// (Fase 31K, ADR 0091), as long as they have not waited half an hour. It runs in the business's own scope (the worker finds
/// which businesses have any); each order reminded raises its event, which the outbox turns into the emails, saved by the
/// caller.
/// </summary>
public sealed class OnlineOrderWaitingPlanner
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _clock;

    public OnlineOrderWaitingPlanner(IApplicationDbContext context, IDateTimeProvider clock)
    {
        _context = context;
        _clock = clock;
    }

    /// <summary>
    /// The orders that are due now: waiting as the bell counts them (<see cref="Order.WaitsForConfirmation"/>), not reminded,
    /// that arrived between half an hour and 15 minutes ago. The worker reads the same across businesses.
    /// </summary>
    public static IQueryable<Order> Due(IQueryable<Order> orders, DateTimeOffset now)
    {
        var arrivedBy = now.AddMinutes(-OrderConfirmTimes.WarnAfterMinutes);
        var arrivedAfter = now.AddMinutes(-OrderConfirmTimes.SlowAfterMinutes);
        return orders.Where(o => o.Channel == OrderChannel.Online
            && o.Status == OrderStatus.Pending
            && !o.CreatedByBusiness
            && o.TableNumber == null
            && o.OpenTableId == null
            && o.ConfirmReminderSentAt == null
            && o.CreatedAt <= arrivedBy
            && o.CreatedAt > arrivedAfter);
    }

    /// <returns>The orders reminded now, not saved yet.</returns>
    public async Task<int> RemindDueAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var reminded = 0;
        foreach (var order in await Due(_context.Orders, now).ToListAsync(cancellationToken))
        {
            if (order.RemindToConfirm(now))
                reminded++;
        }
        return reminded;
    }
}
