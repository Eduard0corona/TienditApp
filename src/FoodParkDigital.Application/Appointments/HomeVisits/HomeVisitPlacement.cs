using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Delivery;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.HomeDelivery;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Appointments.HomeVisits;

/// <summary>
/// Where an appointment at the customer's address happens (Fase 22E), as the team writes it in the agenda: the postal code
/// its delivery zone is found by, the address and, optionally, references to find the place.
/// </summary>
public record AppointmentVisitInput
{
    public string PostalCode { get; init; } = string.Empty;
    public string AddressLine { get; init; } = string.Empty;
    public string? References { get; init; }
}

public class AppointmentVisitInputValidator : AbstractValidator<AppointmentVisitInput>
{
    public AppointmentVisitInputValidator()
    {
        RuleFor(x => x.PostalCode).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(DeliveryAddress.VisitPostalCodeRequiredMessage)
            // Without what was written: a validation message reaches the log.
            .MaximumLength(OrderDeliveryInputValidator.MaxPostalCodeLength).WithMessage(HomeVisitPlacement.PostalCodeMessage);
        RuleFor(x => x.AddressLine).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(DeliveryAddress.VisitAddressLineRequiredMessage)
            .Must(line => line.Trim().Length <= DeliveryAddress.MaxAddressLineLength).WithMessage(DeliveryAddress.AddressLineTooLongMessage);
        RuleFor(x => x.References)
            .Must(references => references is null || references.Trim().Length <= DeliveryAddress.MaxReferencesLength)
            .WithMessage(DeliveryAddress.ReferencesTooLongMessage);
    }
}

/// <summary>
/// Arranges where an appointment happens with the active delivery zone of its business that reaches the postal code, and
/// checks the travel time a team member needs between a visit and their other appointments. The same for booking and for
/// editing an appointment.
/// </summary>
internal static class HomeVisitPlacement
{
    public const string PostalCodeMessage = "El código postal lleva 5 dígitos.";

    /// <summary>
    /// The appointment happens at <paramref name="input"/>: at the same place it keeps what was agreed; at another one,
    /// the zone that reaches it now sets the charge. The message for the team when it cannot go there, or whether the visit
    /// changed.
    /// </summary>
    public static async Task<Result<bool>> ArrangeAsync(
        IApplicationDbContext context,
        Appointment appointment,
        AppointmentVisitInput input,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        try
        {
            var address = DeliveryAddress.ForVisit(input.PostalCode, input.AddressLine, input.References);
            // The same place keeps what was agreed; only its references may change.
            if (appointment.Visit is { } visit && visit.IsAt(address))
                return Result.Success(appointment.UpdateVisitReferences(address, now));

            var zone = await HomeDeliveryPlacement.FindZoneAsync(context, appointment.TenantId, address.PostalCode, cancellationToken);
            if (zone is null)
                return Result.Failure<bool>(await NotCoveredAsync(context, appointment.TenantId, address.PostalCode, cancellationToken));

            return Result.Success(appointment.ArrangeVisit(address, zone, now));
        }
        catch (DomainException ex)
        {
            return Result.Failure<bool>(ex.Message);
        }
    }

    /// <summary>Why the business does not go to <paramref name="postalCode"/>: it has no zone that delivers, or none reaches it.</summary>
    public static async Task<string> NotCoveredAsync(
        IApplicationDbContext context, Guid tenantId, string postalCode, CancellationToken cancellationToken)
        => await DeliveryZoneLookup.AnyActiveAsync(context, tenantId, cancellationToken)
            ? HomeVisitRules.NotCoveredMessage(postalCode)
            : HomeVisitRules.NoZonesMessage;

    /// <summary>
    /// Another open appointment of the same team member closer than the travel time of the business to this one, when one of
    /// the two is a visit at the customer's address: the message for the team, or null. Overlaps are checked before.
    /// </summary>
    /// <param name="staffName">The team member's name as resolved, for the message.</param>
    public static async Task<string?> TravelConflictAsync(
        IApplicationDbContext context,
        Guid tenantId,
        Guid? staffUserId,
        string? staffName,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        bool isVisit,
        Guid? excludeAppointmentId,
        CancellationToken cancellationToken)
    {
        if (staffUserId is null && string.IsNullOrWhiteSpace(staffName))
            return null;

        var minutes = await context.TenantSettings
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .Select(s => (int?)s.HomeVisitTravelMinutes)
            .FirstOrDefaultAsync(cancellationToken) ?? HomeVisitRules.DefaultTravelMinutes;
        if (minutes == 0)
            return null;

        var from = startAt.AddMinutes(-minutes);
        var to = endAt.AddMinutes(minutes);
        var query = context.Appointments
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .Where(x => x.Status != AppointmentStatus.Cancelled && x.Status != AppointmentStatus.NoShow)
            .Where(x => from < x.EndAt && to > x.StartAt);
        // At the business, only a visit of the same person needs the trip; a visit needs it with any other appointment.
        if (!isVisit)
            query = query.Where(x => x.Visit != null);
        if (excludeAppointmentId is { } excluded)
            query = query.Where(x => x.Id != excluded);
        query = staffUserId is { } userId
            ? query.Where(x => x.StaffUserId == userId)
            : query.Where(x => x.StaffUserId == null && x.StaffName == staffName);

        return await query.AnyAsync(cancellationToken)
            ? HomeVisitRules.TravelMessage(string.IsNullOrWhiteSpace(staffName) ? "El colaborador" : staffName, minutes)
            : null;
    }
}

/// <summary>
/// The activity entry of where an appointment happens: the zone and the charge agreed, never the customer's address (it is
/// theirs, and the activity is read by the whole management of the business).
/// </summary>
internal static class AppointmentVisitAudit
{
    public const string Arranged = "AppointmentVisitArranged";
    public const string Removed = "AppointmentVisitRemoved";

    public static AuditLog Create(Appointment appointment, string? performedBy)
        => new(
            tenantId: appointment.TenantId,
            entityName: nameof(Appointment),
            entityId: appointment.Id.ToString(),
            action: appointment.Visit is null ? Removed : Arranged,
            performedBy: performedBy ?? "system",
            metadata: appointment.Visit is { } visit ? JsonSerializer.Serialize(new { visit.ZoneName, visit.Fee }) : null);
}
