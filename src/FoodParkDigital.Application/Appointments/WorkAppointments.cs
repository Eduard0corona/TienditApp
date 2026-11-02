using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Appointments.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Appointments;

/// <summary>One appointment that does (part of) the job of a service order (Fase 29F): when, who and how it goes.</summary>
public class WorkAppointmentDto
{
    public Guid Id { get; set; }
    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset EndAt { get; set; }
    public string? StaffName { get; set; }
    public AppointmentStatus Status { get; set; }
    /// <summary>It happens at the customer's address (the place of the visit the quote came from).</summary>
    public bool AtCustomersAddress { get; set; }
}

/// <summary>
/// The appointments that do the job of service orders (Fase 29F, ADR 0053 point 17), and the folio of the order each
/// appointment works on: one query each for what is shown, by the index of the order an appointment works on.
/// </summary>
internal static class WorkAppointments
{
    /// <summary>The appointments of the job of each order, in the order of their time, cancelled ones included.</summary>
    public static async Task<Dictionary<Guid, List<WorkAppointmentDto>>> ForOrdersAsync(
        IApplicationDbContext context, Guid tenantId, IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken)
    {
        if (orderIds.Count == 0)
            return [];

        var appointments = await context.Appointments
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.WorkOrderId != null && orderIds.Contains(a.WorkOrderId.Value))
            .OrderBy(a => a.StartAt)
            .Select(a => new
            {
                OrderId = a.WorkOrderId!.Value,
                Appointment = new WorkAppointmentDto
                {
                    Id = a.Id,
                    StartAt = a.StartAt,
                    EndAt = a.EndAt,
                    StaffName = a.StaffName,
                    Status = a.Status,
                    AtCustomersAddress = a.Visit != null
                }
            })
            .ToListAsync(cancellationToken);
        return appointments
            .GroupBy(a => a.OrderId)
            .ToDictionary(group => group.Key, group => group.Select(a => a.Appointment).ToList());
    }

    /// <summary>
    /// The folio of each order an appointment works on, for the agenda and the route: orders are filtered by business, and the
    /// ids come from appointments that already were, like the sales of <see cref="AppointmentSales"/>.
    /// </summary>
    public static async Task<Dictionary<Guid, string>> OrderNumbersAsync(
        IApplicationDbContext context, IEnumerable<Guid?> workOrderIds, CancellationToken cancellationToken)
    {
        var ids = workOrderIds.OfType<Guid>().Distinct().ToList();
        if (ids.Count == 0)
            return [];

        return await context.Orders
            .AsNoTracking()
            .Where(o => ids.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.OrderNumber, cancellationToken);
    }
}
