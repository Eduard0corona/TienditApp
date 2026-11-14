using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.OnlinePayments.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders;

/// <summary>
/// A new sale that charges an appointment of the agenda (Fase 22E): the appointment is done with it, and when it was at the
/// customer's address its charge for the visit enters the total (<see cref="Order.ChargeVisit"/>). Since the Fase 31O (ADR
/// 0095), the deposit its customer paid online to book it, while held, is the sale's «En línea» payment
/// (<see cref="Order.RegisterAppointmentDeposit"/>): the rest is charged as always. The appointment changes with the sale, so
/// two charges of it saved at the same time collide on its version (and on its deposit's checkout).
/// </summary>
internal static class AppointmentSalePlacement
{
    public const string DepositNotFoundMessage = "No encontramos el pago del anticipo de esta cita: revisa Pagos en línea antes de cobrarla.";

    /// <summary>
    /// Charges the appointment with <paramref name="order"/>, after its lines and before any payment or change of status;
    /// the message for the team when it cannot, or null.
    /// </summary>
    /// <param name="feeTaxRate">The IVA rate of the business (TenantSalesSettings.DefaultTaxRate).</param>
    /// <param name="now">When the sale is charged: the day its deposit enters it.</param>
    public static async Task<string?> ChargeAsync(
        IApplicationDbContext context, Order order, Guid appointmentId, decimal feeTaxRate, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // The business of the sale besides the global filter: one of another business is not found.
        var appointment = await context.Appointments
            .FirstOrDefaultAsync(a => a.Id == appointmentId && a.TenantId == order.TenantId, cancellationToken);
        if (appointment is null)
            return CreateOrderCommandValidator.AppointmentNotFoundMessage;

        // A sale that was cancelled no longer charges it: the appointment can be charged again.
        var previousSaleCancelled = appointment.SaleOrderId is { } previousSale
            && await context.Orders.AsNoTracking().AnyAsync(o => o.Id == previousSale && o.Status == OrderStatus.Cancelled, cancellationToken);
        // The deposit it was booked with, still held: the payment that paid it, read before the appointment takes it.
        var deposit = appointment.HoldsDeposit
            ? await context.OnlineCheckouts.FirstOrDefaultAsync(
                c => c.TenantId == order.TenantId && c.ForAppointment && c.AppointmentId == appointment.Id && c.Status == OnlineCheckoutStatus.Placed,
                cancellationToken)
            : null;
        if (appointment.HoldsDeposit && deposit?.PaymentId is null)
            return DepositNotFoundMessage;
        try
        {
            appointment.RecordSale(order.Id, previousSaleCancelled);
            if (appointment.Visit is { Fee: > 0 } visit)
                order.ChargeVisit(appointment.Id, visit.ZoneName, visit.Fee, VatRate.Of(feeTaxRate));
            if (deposit is not null)
            {
                order.RegisterAppointmentDeposit(deposit.PaymentId!, appointment.DepositAmount!.Value, now, inventoryLocationId: null);
                deposit.ApplyToSale(order.Id);
            }
            return null;
        }
        catch (DomainException ex)
        {
            return ex.Message;
        }
    }
}
