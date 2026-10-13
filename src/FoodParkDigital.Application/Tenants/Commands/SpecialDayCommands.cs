using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Tenants.DTOs;
using FoodParkDigital.Application.Tenants.Hours;
using FoodParkDigital.Application.Tenants.Queries;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tenants.Commands;

/// <summary>
/// Marks dates the business closes or opens with other hours (owners and admins): one date, or consecutive dates with
/// one rule, from its today on in its time zone. The public menu and the WhatsApp replies apply them.
/// </summary>
[RequiresTenantManager]
public record AddSpecialDayCommand(DateOnly From, DateOnly? To, SpecialDayKind Kind, List<SpecialOpeningDto>? Openings, string? Reason)
    : IRequest<Result<SpecialDaysDto>>, ISpecialDayRequest;

/// <summary>
/// Moves a period of special days or gives it another rule (owners and admins). The dates that passed stay as they were:
/// a period under way keeps its first date, and a new rule for it starts today.
/// </summary>
[RequiresTenantManager]
public record UpdateSpecialDayCommand(
    Guid Id, int Revision, DateOnly From, DateOnly? To, SpecialDayKind Kind, List<SpecialOpeningDto>? Openings, string? Reason)
    : IRequest<Result<SpecialDaysDto>>, ISpecialDayRequest;

/// <summary>Removes a period of special days (owners and admins); one under way ends yesterday instead.</summary>
[RequiresTenantManager]
public record RemoveSpecialDayCommand(Guid Id, int Revision) : IRequest<Result<SpecialDaysDto>>;

public static class SpecialDayErrors
{
    /// <summary>The period changed since the owner loaded it: the same text as a change saved at the same time (409).</summary>
    public const string Stale = "Alguien más modificó este registro al mismo tiempo. Recarga e intenta de nuevo.";

    public const string TakenMeanwhile = "Otra persona marcó una de esas fechas al mismo tiempo. Recarga para verla.";
}

/// <summary>What adding and changing a period share: its dates and its rule.</summary>
internal interface ISpecialDayRequest
{
    DateOnly From { get; }
    DateOnly? To { get; }
    SpecialDayKind Kind { get; }
    List<SpecialOpeningDto>? Openings { get; }
    string? Reason { get; }
}

public class AddSpecialDayCommandValidator : AbstractValidator<AddSpecialDayCommand>
{
    public AddSpecialDayCommandValidator()
    {
        this.ValidateTheDay();
    }
}

public class UpdateSpecialDayCommandValidator : AbstractValidator<UpdateSpecialDayCommand>
{
    public UpdateSpecialDayCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage(SpecialDayRequestRules.ReloadMessage);
        RuleFor(x => x.Revision).GreaterThan(0).WithMessage(SpecialDayRequestRules.ReloadMessage);
        this.ValidateTheDay();
    }
}

public class RemoveSpecialDayCommandValidator : AbstractValidator<RemoveSpecialDayCommand>
{
    public RemoveSpecialDayCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage(SpecialDayRequestRules.ReloadMessage);
        RuleFor(x => x.Revision).GreaterThan(0).WithMessage(SpecialDayRequestRules.ReloadMessage);
    }
}

/// <summary>
/// The shape of a special day as it arrives. What depends on the business's today (dates ahead, overlaps, the limits)
/// is decided by <see cref="SpecialDayCalendar"/>.
/// </summary>
internal static class SpecialDayRequestRules
{
    public const string ReloadMessage = "Recarga para ver la versión actual del día especial.";
    private const string OpensMessage = "Escribe la hora de apertura como 09:00.";
    private const string ClosesMessage = "Escribe la hora de cierre como 20:00.";

    public static void ValidateTheDay<T>(this AbstractValidator<T> validator) where T : ISpecialDayRequest
    {
        validator.RuleFor(x => x.From).NotEmpty().WithMessage("Elige la fecha.");
        validator.RuleFor(x => x.To)
            .Must((request, to) => to is null || to >= request.From)
            .WithMessage("La fecha final no puede ser antes de la inicial.");
        validator.RuleFor(x => x.Kind).IsInEnum().WithMessage("El tipo de día especial no existe.");
        validator.RuleFor(x => x.Openings)
            .Empty()
            .WithMessage("Un día cerrado no lleva horario.")
            .When(x => x.Kind == SpecialDayKind.Closed);
        validator.RuleFor(x => x.Openings)
            .Must(openings => openings is { Count: >= 1 and <= SpecialDayRule.MaxOpenings })
            .WithMessage("Escribe uno o dos horarios para ese día.")
            .When(x => x.Kind == SpecialDayKind.SpecialHours);
        validator.RuleForEach(x => x.Openings)
            .NotNull()
            .WithMessage(OpensMessage)
            .ChildRules(opening =>
            {
                opening.RuleFor(o => o.Opens).Cascade(CascadeMode.Stop)
                    .NotEmpty().WithMessage(OpensMessage)
                    .Matches(OpeningHoursMappings.TimePattern).WithMessage(OpensMessage);
                opening.RuleFor(o => o.Closes).Cascade(CascadeMode.Stop)
                    .NotEmpty().WithMessage(ClosesMessage)
                    .Matches(OpeningHoursMappings.TimePattern).WithMessage(ClosesMessage);
            });
        // As the domain reads it: trimmed, on one line (it goes in the menu's chip and in WhatsApp).
        validator.RuleFor(x => x.Reason)
            .Must(reason => reason!.Trim().Length <= SpecialDayRule.MaxReasonLength
                && !reason.Any(c => char.IsControl(c) || c is '\u2028' or '\u2029'))
            .WithMessage("El motivo tiene máximo 40 caracteres y va en una sola línea.")
            .When(x => x.Reason is not null);
    }
}

public class AddSpecialDayCommandHandler : IRequestHandler<AddSpecialDayCommand, Result<SpecialDaysDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICurrentTenantService _tenantService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public AddSpecialDayCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        ICurrentTenantService tenantService,
        IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _currentUserService = currentUserService;
        _tenantService = tenantService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<SpecialDaysDto>> Handle(AddSpecialDayCommand request, CancellationToken cancellationToken)
    {
        var tenantId = await MyTenantResolver.ResolveAsync(_context, _tenantService, _currentUserService, cancellationToken);
        if (tenantId is null)
            return Result.Failure<SpecialDaysDto>("No se encontró el negocio del usuario.");

        var (today, weekly) = await SpecialDaysReader.TodayAsync(_context, tenantId.Value, _dateTimeProvider.UtcNow, cancellationToken);
        var rule = SpecialDayMappings.ToRule(request.Kind, request.Openings, request.Reason);
        // Only read: adding never changes the dates already there.
        var rows = await _context.SpecialDays
            .AsNoTracking()
            .Where(day => day.TenantId == tenantId.Value && day.Date >= today)
            .ToListAsync(cancellationToken);

        var change = new SpecialDayCalendar(tenantId.Value, today, rows).Add(request.From, request.To ?? request.From, rule);
        var error = await SpecialDayChanges.SaveAsync(
            _context, tenantId.Value, change, SpecialDayAudit.Added, _currentUserService.UserName, cancellationToken);
        if (error is not null)
            return Result.Failure<SpecialDaysDto>(error);

        return Result.Success(await SpecialDaysReader.LoadAsync(_context, tenantId.Value, today, weekly, cancellationToken));
    }
}

public class UpdateSpecialDayCommandHandler : IRequestHandler<UpdateSpecialDayCommand, Result<SpecialDaysDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICurrentTenantService _tenantService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public UpdateSpecialDayCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        ICurrentTenantService tenantService,
        IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _currentUserService = currentUserService;
        _tenantService = tenantService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<SpecialDaysDto>> Handle(UpdateSpecialDayCommand request, CancellationToken cancellationToken)
    {
        var tenantId = await MyTenantResolver.ResolveAsync(_context, _tenantService, _currentUserService, cancellationToken);
        if (tenantId is null)
            return Result.Failure<SpecialDaysDto>("No se encontró el negocio del usuario.");

        var (today, weekly) = await SpecialDaysReader.TodayAsync(_context, tenantId.Value, _dateTimeProvider.UtcNow, cancellationToken);
        var rule = SpecialDayMappings.ToRule(request.Kind, request.Openings, request.Reason);
        var rows = await SpecialDayChanges.LoadForAsync(_context, tenantId.Value, request.Id, today, cancellationToken);
        if (SpecialDaysReader.RevisionOf(rows, request.Id) != request.Revision)
            return Result.Failure<SpecialDaysDto>(SpecialDayErrors.Stale);

        var change = new SpecialDayCalendar(tenantId.Value, today, rows)
            .Change(request.Id, request.From, request.To ?? request.From, rule);
        var error = await SpecialDayChanges.SaveAsync(
            _context, tenantId.Value, change, SpecialDayAudit.Updated, _currentUserService.UserName, cancellationToken);
        if (error is not null)
            return Result.Failure<SpecialDaysDto>(error);

        return Result.Success(await SpecialDaysReader.LoadAsync(_context, tenantId.Value, today, weekly, cancellationToken));
    }
}

public class RemoveSpecialDayCommandHandler : IRequestHandler<RemoveSpecialDayCommand, Result<SpecialDaysDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICurrentTenantService _tenantService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public RemoveSpecialDayCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        ICurrentTenantService tenantService,
        IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _currentUserService = currentUserService;
        _tenantService = tenantService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<SpecialDaysDto>> Handle(RemoveSpecialDayCommand request, CancellationToken cancellationToken)
    {
        var tenantId = await MyTenantResolver.ResolveAsync(_context, _tenantService, _currentUserService, cancellationToken);
        if (tenantId is null)
            return Result.Failure<SpecialDaysDto>("No se encontró el negocio del usuario.");

        var (today, weekly) = await SpecialDaysReader.TodayAsync(_context, tenantId.Value, _dateTimeProvider.UtcNow, cancellationToken);
        var rows = await SpecialDayChanges.LoadForAsync(_context, tenantId.Value, request.Id, today, cancellationToken);
        if (SpecialDaysReader.RevisionOf(rows, request.Id) != request.Revision)
            return Result.Failure<SpecialDaysDto>(SpecialDayErrors.Stale);

        var change = new SpecialDayCalendar(tenantId.Value, today, rows).Remove(request.Id);
        // Only deletes: no date can be taken meanwhile.
        var error = await SpecialDayChanges.SaveAsync(
            _context, tenantId.Value, change, SpecialDayAudit.Removed, _currentUserService.UserName, cancellationToken);
        if (error is not null)
            return Result.Failure<SpecialDaysDto>(error);

        return Result.Success(await SpecialDaysReader.LoadAsync(_context, tenantId.Value, today, weekly, cancellationToken));
    }
}

/// <summary>Loading and saving the rows a change of special days touches.</summary>
internal static class SpecialDayChanges
{
    /// <summary>
    /// The business's dates from today on and every date of the period (past ones included), tracked to change them.
    /// A period of another business, or one that does not exist, is not found.
    /// </summary>
    public static async Task<List<SpecialDay>> LoadForAsync(
        IApplicationDbContext context, Guid tenantId, Guid periodId, DateOnly today, CancellationToken cancellationToken)
    {
        var rows = await context.SpecialDays
            .Where(day => day.TenantId == tenantId && (day.Date >= today || day.PeriodId == periodId))
            .ToListAsync(cancellationToken);
        if (!rows.Any(row => row.PeriodId == periodId))
            throw new EntityNotFoundException(nameof(SpecialDay), periodId);
        return rows;
    }

    /// <summary>
    /// Saves a change with its activity entry, in one SaveChanges; a request that changes nothing saves and records
    /// nothing. Null when saved, or the error to answer.
    /// </summary>
    public static async Task<string?> SaveAsync(
        IApplicationDbContext context,
        Guid tenantId,
        SpecialDayChange change,
        string action,
        string? performedBy,
        CancellationToken cancellationToken)
    {
        if (change.IsEmpty)
            return null;

        context.SpecialDays.AddRange(change.Added);
        context.SpecialDays.RemoveRange(change.Removed);
        context.AuditLogs.Add(SpecialDayAudit.Create(tenantId, action, change, performedBy ?? "system"));
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception is not DbUpdateConcurrencyException && change.Added.Count > 0)
        {
            // A date was marked at the same time (IX_SpecialDays_TenantId_Date). This copy is undone whole, its activity
            // entry included, and what is stored now decides: the same period (a second tap) is the answer.
            await context.DiscardUnitOfWorkAsync(cancellationToken);
            if (change.After is { } after && await SpecialDaysReader.HasPeriodAsync(context, tenantId, after, cancellationToken))
                return null;
            var dates = change.Added.Select(day => day.Date).ToList();
            if (await context.SpecialDays.AnyAsync(day => day.TenantId == tenantId && dates.Contains(day.Date), cancellationToken))
                return SpecialDayErrors.TakenMeanwhile;
            throw;
        }

        return null;
    }
}

/// <summary>Who changed a period of special days, and from what: they decide when customers can order online.</summary>
internal static class SpecialDayAudit
{
    public const string Added = "SpecialDayAdded";
    public const string Updated = "SpecialDayUpdated";
    public const string Removed = "SpecialDayRemoved";

    public static AuditLog Create(Guid tenantId, string action, SpecialDayChange change, string performedBy)
    {
        object metadata = action switch
        {
            Added => new { Period = change.PeriodId, New = Describe(change.After) },
            Updated => new
            {
                Period = change.PeriodId,
                Previous = Describe(change.Before),
                New = Describe(change.After),
                // A new rule for a period under way: its dates from today on moved to this period.
                NewPeriod = change.NewPeriodId
            },
            Removed => new { Period = change.PeriodId, Previous = Describe(change.Before), RemovedFrom = change.Removed.Min(day => day.Date) },
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown special day action.")
        };

        return new AuditLog(
            tenantId: tenantId,
            entityName: nameof(SpecialDay),
            entityId: change.PeriodId.ToString(),
            action: action,
            performedBy: performedBy,
            metadata: JsonSerializer.Serialize(metadata));
    }

    private static string? Describe(SpecialDayPeriodSnapshot? period) => period is null ? null : SpecialDayText.Describe(period);
}
