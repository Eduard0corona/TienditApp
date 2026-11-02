using FoodParkDigital.Application.Appointments;
using FoodParkDigital.Application.Appointments.HomeVisits;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Appointments;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Quotes;

/// <summary>
/// The business agenda the job of the service order an accepted quote became (Fase 29F, ADR 0053 point 17), or part of it:
/// an appointment of the team with whom and when, at the place of the visit the quote came from when there was one. The job
/// is charged in its order, so the appointment is never charged on its own. It takes more than one appointment when the
/// job does.
/// </summary>
[RequiresPermission(TenantPermission.ManageAppointments)]
[RequiresCapability(TenantCapability.Appointments)]
public record ScheduleQuoteWorkCommand(
    Guid QuoteId, DateTimeOffset StartAt, DateTimeOffset EndAt, Guid? StaffUserId, string? Notes) : IRequest<Result<QuoteDto>>;

public class ScheduleQuoteWorkCommandValidator : AbstractValidator<ScheduleQuoteWorkCommand>
{
    public ScheduleQuoteWorkCommandValidator()
    {
        RuleFor(x => x.QuoteId).NotEmpty();
        RuleFor(x => x.EndAt).GreaterThan(x => x.StartAt).WithMessage(QuoteWork.EndAfterStartMessage);
        RuleFor(x => x).Must(x => x.EndAt - x.StartAt <= QuoteWork.LongestAppointment).WithMessage(QuoteWork.TooLongMessage)
            .When(x => x.EndAt > x.StartAt);
        RuleFor(x => x.StaffUserId).Must(id => id != Guid.Empty).When(x => x.StaffUserId is not null);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

/// <summary>The rules and messages of agendar the job of a quote.</summary>
public static class QuoteWork
{
    /// <summary>A job that takes longer is agendado in more than one appointment, one per day.</summary>
    public static readonly TimeSpan LongestAppointment = TimeSpan.FromHours(12);
    public const string EndAfterStartMessage = "El trabajo termina después de empezar.";
    public const string TooLongMessage = "Una cita del trabajo dura hasta 12 horas: agenda los demás días en otra.";
    public const string NotConvertedMessage = "Primero convierte la cotización aceptada en su orden de servicio.";
    public const string OverlapMessage = "Ya existe una cita para ese colaborador en el horario seleccionado.";

    public static string OrderCancelledMessage(string orderNumber) => $"La orden {orderNumber} se canceló: su trabajo ya no se agenda.";
}

public class ScheduleQuoteWorkCommandHandler : IRequestHandler<ScheduleQuoteWorkCommand, Result<QuoteDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly AppointmentNotices _notices;
    private readonly IOutgoingMessageSignal? _signal;

    public ScheduleQuoteWorkCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUser,
        IDateTimeProvider clock,
        AppointmentNotices notices,
        IOutgoingMessageSignal? signal = null)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUser = currentUser;
        _clock = clock;
        _notices = notices;
        _signal = signal;
    }

    private Guid TenantId => _tenantService.TenantId;

    public async Task<Result<QuoteDto>> Handle(ScheduleQuoteWorkCommand request, CancellationToken cancellationToken)
    {
        var quote = await QuotesHandler.FindAsync(_context, TenantId, request.QuoteId, cancellationToken, tracking: false);
        if (quote.OrderId is not { } orderId)
            return Result.Failure<QuoteDto>(QuoteWork.NotConvertedMessage);
        var order = await _context.Orders
            .AsNoTracking()
            .Where(o => o.Id == orderId && o.TenantId == TenantId)
            .Select(o => new { o.Id, o.OrderNumber, o.Status, o.CustomerName, o.CustomerPhone })
            .FirstAsync(cancellationToken);
        if (order.Status == OrderStatus.Cancelled)
            return Result.Failure<QuoteDto>(QuoteWork.OrderCancelledMessage(order.OrderNumber));

        var serviceName = AppointmentWorkRules.ServiceName(quote.Number);
        var resources = await AppointmentResources.ResolveAsync(
            _context, null, serviceName, request.StaffUserId, null, cancellationToken);
        if (resources.IsFailure)
            return Result.Failure<QuoteDto>(resources.Error!);
        if (await AppointmentResources.OverlapsAsync(
                _context, request.StaffUserId, resources.Value!.StaffName, request.StartAt, request.EndAt, null, cancellationToken))
            return Result.Failure<QuoteDto>(QuoteWork.OverlapMessage);

        // Where the customer was visited, when the quote came from a visit: the job happens there (copied, nothing to charge).
        var visit = quote.AppointmentId is { } visitId
            ? await _context.Appointments
                .AsNoTracking()
                .Where(a => a.Id == visitId && a.TenantId == TenantId)
                .Select(a => a.Visit)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        Appointment appointment;
        try
        {
            // The order's customer, or the phone as the quote wrote it when the order has none (a phone that did not read).
            appointment = Appointment.ForWork(
                TenantId, order.Id, order.CustomerName ?? quote.CustomerName, order.CustomerPhone ?? quote.CustomerPhone,
                serviceName, request.StartAt, request.EndAt, visit, resources.Value.StaffName, request.StaffUserId, request.Notes);
        }
        catch (DomainException ex)
        {
            return Result.Failure<QuoteDto>(ex.Message);
        }

        var travel = await HomeVisitPlacement.TravelConflictAsync(
            _context, TenantId, request.StaffUserId, resources.Value.StaffName, request.StartAt, request.EndAt,
            appointment.IsHomeVisit, null, cancellationToken);
        if (travel is not null)
            return Result.Failure<QuoteDto>(travel);

        _context.Appointments.Add(appointment);
        _context.AuditLogs.Add(QuoteAudit.Create(quote, QuoteAudit.WorkScheduled, _currentUser.UserName, new
        {
            OrderId = order.Id,
            AppointmentId = appointment.Id,
            appointment.StartAt,
            appointment.EndAt,
            appointment.StaffUserId,
            AtCustomersAddress = appointment.IsHomeVisit
        }));
        var noticeQueued = await _notices.QueueScheduledAsync(appointment, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        if (noticeQueued)
            _signal?.Notify();

        return Result.Success(await QuoteViews.DescribeAsync(
            _context, quote, await QuoteCalendar.TodayAsync(_context, TenantId, _clock.UtcNow, cancellationToken), cancellationToken));
    }
}
