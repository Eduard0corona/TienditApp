using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Tenants.DTOs;
using FoodParkDigital.Application.Tenants.Hours;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tenants.Queries;

/// <summary>The special days of the business, with the holidays it may want to mark (owners and admins).</summary>
[RequiresTenantManager]
public record GetSpecialDaysQuery : IRequest<Result<SpecialDaysDto>>;

public class GetSpecialDaysQueryHandler : IRequestHandler<GetSpecialDaysQuery, Result<SpecialDaysDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICurrentTenantService _tenantService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public GetSpecialDaysQueryHandler(
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

    public async Task<Result<SpecialDaysDto>> Handle(GetSpecialDaysQuery request, CancellationToken cancellationToken)
    {
        var tenantId = await MyTenantResolver.ResolveAsync(_context, _tenantService, _currentUserService, cancellationToken);
        if (tenantId is null)
            return Result.Failure<SpecialDaysDto>("No se encontró el negocio del usuario.");

        var (today, weekly) = await SpecialDaysReader.TodayAsync(_context, tenantId.Value, _dateTimeProvider.UtcNow, cancellationToken);
        return Result.Success(await SpecialDaysReader.LoadAsync(_context, tenantId.Value, today, weekly, cancellationToken));
    }
}

/// <summary>The special days of a business as its settings show them, and what its changes need to know.</summary>
internal static class SpecialDaysReader
{
    /// <summary>The business's date in its time zone (special days are its dates), and its weekly hours.</summary>
    public static async Task<(DateOnly Today, OpeningHours? Weekly)> TodayAsync(
        IApplicationDbContext context, Guid tenantId, DateTimeOffset utcNow, CancellationToken cancellationToken)
    {
        var settings = await context.TenantSettings
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .Select(s => new { s.TimeZone, s.OpeningHours })
            .FirstOrDefaultAsync(cancellationToken);
        var today = DateOnly.FromDateTime(TenantDateTimeResolver.LocalNow(utcNow, settings?.TimeZone));
        return (today, settings?.OpeningHours);
    }

    public static async Task<SpecialDaysDto> LoadAsync(
        IApplicationDbContext context, Guid tenantId, DateOnly today, OpeningHours? weekly, CancellationToken cancellationToken)
    {
        // A past period shown ended at most 90 days ago and lasted at most 92 days: none of its dates is older than this.
        var since = today.AddDays(-(SpecialDay.PastDaysShown + SpecialDay.MaxDaysPerPeriod - 1));
        var rows = await context.SpecialDays
            .AsNoTracking()
            .Where(day => day.TenantId == tenantId && day.Date >= since)
            .OrderBy(day => day.Date)
            .ToListAsync(cancellationToken);

        var periods = SpecialDayPeriods.Group(rows);
        var upcomingDates = rows.Where(row => row.Date >= today).Select(row => row.Date).ToHashSet();
        return new SpecialDaysDto
        {
            Today = today,
            HasWeeklyHours = weekly is not null,
            Upcoming = periods.Where(period => period.To >= today).Select(period => ToDto(period, today)).ToList(),
            Past = periods
                .Where(period => period.To < today && period.To >= today.AddDays(-SpecialDay.PastDaysShown))
                .OrderByDescending(period => period.From)
                .Select(period => ToDto(period, today))
                .ToList(),
            Suggestions = MexicanHolidays.Suggest(today, weekly, upcomingDates),
            UpcomingDates = upcomingDates.Count,
            MaxUpcomingDates = SpecialDay.MaxUpcomingDates,
            MaxDaysPerPeriod = SpecialDay.MaxDaysPerPeriod,
            MaxDaysAhead = SpecialDay.MaxDaysAhead,
            MaxReasonLength = SpecialDayRule.MaxReasonLength
        };
    }

    /// <summary>
    /// Whether one period holds exactly these dates with this rule: the change a concurrent request (a second tap)
    /// already saved.
    /// </summary>
    public static async Task<bool> HasPeriodAsync(
        IApplicationDbContext context, Guid tenantId, SpecialDayPeriodSnapshot period, CancellationToken cancellationToken)
    {
        var rows = await context.SpecialDays
            .AsNoTracking()
            .Where(day => day.TenantId == tenantId && day.Date >= period.From && day.Date <= period.To)
            .ToListAsync(cancellationToken);
        if (rows.Count != period.To.DayNumber - period.From.DayNumber + 1
            || rows.Select(row => row.PeriodId).Distinct().Count() != 1
            || rows.Any(row => row.Rule != period.Rule))
            return false;

        var periodId = rows[0].PeriodId;
        return !await context.SpecialDays.AnyAsync(
            day => day.TenantId == tenantId && day.PeriodId == periodId && (day.Date < period.From || day.Date > period.To),
            cancellationToken);
    }

    /// <summary>The revision of a period the owner sees, the one its dates from today on share.</summary>
    public static int RevisionOf(IEnumerable<SpecialDay> rows, Guid periodId)
        => rows.Where(row => row.PeriodId == periodId).Max(row => row.Revision);

    private static SpecialDayDto ToDto(SpecialDayPeriod period, DateOnly today) => new()
    {
        Id = period.Id,
        From = period.From,
        To = period.To,
        Kind = period.Rule.Kind,
        Openings = period.Rule.Openings.ToDtos(),
        Reason = period.Rule.Reason,
        Revision = period.Revision,
        InProgress = period.From < today && today <= period.To
    };
}
