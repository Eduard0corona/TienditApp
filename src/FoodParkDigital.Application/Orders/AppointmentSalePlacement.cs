using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders;

/// <summary>
/// A new sale that charges an appointment of the agenda (Fase 22E): the appointment is done with it, and when it was at the
/// customer's address its charge for the visit enters the total (<see cref="Order.ChargeVisit"/>). The appointment changes
/// with the sale, so two charges of it saved at the same time collide on its version.
/// </summary>
internal static class AppointmentSalePlacement
{
    /// <summary>
    /// Charges the appointment with <paramref name="order"/>, after its lines and before any payment or change of status;
    /// the message for the team when it cannot, or null.
    /// </summary>
    /// <param name="feeTaxRate">The IVA rate of the business (TenantSalesSettings.DefaultTaxRate).</param>
    public static async Task<string?> ChargeAsync(
        IApplicationDbContext context, Order order, Guid appointmentId, decimal feeTaxRate, CancellationToken cancellationToken)
    {
        // Appointments are filtered by business: one of another business is not found.
        var appointment = await context.Appointments.FirstOrDefaultAsync(a => a.Id == appointmentId, cancellationToken);
        if (appointment is null)
            return CreateOrderCommandValidator.AppointmentNotFoundMessage;

        // A sale that was cancelled no longer charges it: the appointment can be charged again.
        var previousSaleCancelled = appointment.SaleOrderId is { } previousSale
            && await context.Orders.AsNoTracking().AnyAsync(o => o.Id == previousSale && o.Status == OrderStatus.Cancelled, cancellationToken);
        try
        {
            appointment.RecordSale(order.Id, previousSaleCancelled);
            if (appointment.Visit is { Fee: > 0 } visit)
                order.ChargeVisit(appointment.Id, visit.ZoneName, visit.Fee, VatRate.Of(feeTaxRate));
            return null;
        }
        catch (DomainException ex)
        {
            return ex.Message;
        }
    }
}
