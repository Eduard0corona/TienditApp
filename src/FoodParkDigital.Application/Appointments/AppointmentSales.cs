using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Orders.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Appointments;

/// <summary>
/// The sales of the appointments that were cancelled (Fase 22E): such an appointment is charged again with another sale, so
/// the agenda offers «Cobrar» instead of saying it has its sale.
/// </summary>
internal static class AppointmentSales
{
    public static async Task<IReadOnlySet<Guid>> CancelledAsync(
        IApplicationDbContext context, IEnumerable<Guid?> saleOrderIds, CancellationToken cancellationToken)
    {
        var ids = saleOrderIds.OfType<Guid>().Distinct().ToList();
        if (ids.Count == 0)
            return new HashSet<Guid>();

        return (await context.Orders
                .AsNoTracking()
                .Where(o => ids.Contains(o.Id) && o.Status == OrderStatus.Cancelled)
                .Select(o => o.Id)
                .ToListAsync(cancellationToken))
            .ToHashSet();
    }
}
