using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Tenants.DTOs;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tenants.Commands;

/// <summary>
/// The hours of the business (owners and admins), in its time zone. Without openings the business has no hours
/// and never shows as closed. With them, the public menu takes pickup and delivery orders only while it is open.
/// </summary>
[RequiresTenantManager]
public record UpdateOpeningHoursCommand(List<OpeningIntervalDto>? Intervals) : IRequest<Result<OpeningHoursDto>>;

public class UpdateOpeningHoursCommandValidator : AbstractValidator<UpdateOpeningHoursCommand>
{
    private const string TimePattern = "^([01][0-9]|2[0-3]):[0-5][0-9]$";

    public UpdateOpeningHoursCommandValidator()
    {
        RuleFor(x => x.Intervals!.Count)
            .LessThanOrEqualTo(7 * OpeningHours.MaxIntervalsPerDay)
            .WithMessage("Un horario admite como máximo dos horarios por día.")
            .When(x => x.Intervals is not null);
        RuleForEach(x => x.Intervals).ChildRules(interval =>
        {
            interval.RuleFor(i => i.Day).IsInEnum().WithMessage("El día del horario no existe.");
            interval.RuleFor(i => i.Opens).NotEmpty().Matches(TimePattern).WithMessage("Escribe la hora de apertura como 09:00.");
            interval.RuleFor(i => i.Closes).NotEmpty().Matches(TimePattern).WithMessage("Escribe la hora de cierre como 20:00.");
        });
    }
}

public class UpdateOpeningHoursCommandHandler : IRequestHandler<UpdateOpeningHoursCommand, Result<OpeningHoursDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICurrentTenantService _tenantService;

    public UpdateOpeningHoursCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        ICurrentTenantService tenantService)
    {
        _context = context;
        _currentUserService = currentUserService;
        _tenantService = tenantService;
    }

    public async Task<Result<OpeningHoursDto>> Handle(UpdateOpeningHoursCommand request, CancellationToken cancellationToken)
    {
        var tenantId = await MyTenantResolver.ResolveAsync(_context, _tenantService, _currentUserService, cancellationToken);
        if (tenantId is null)
            return Result.Failure<OpeningHoursDto>("No se encontró el negocio del usuario.");

        // The rules of the domain (overlaps, two openings a day) explain what is wrong before anything changes.
        var hours = request.Intervals is { Count: > 0 } intervals ? OpeningHours.Create(intervals.ToIntervals()) : null;

        var settings = await _context.TenantSettings
            .FirstOrDefaultAsync(x => x.TenantId == tenantId.Value, cancellationToken);
        if (settings is null)
        {
            // Settings missing (businesses from before they existed): start from what the business had.
            var businessType = await _context.Tenants
                .Where(t => t.Id == tenantId.Value)
                .Select(t => (BusinessType?)t.BusinessType)
                .FirstOrDefaultAsync(cancellationToken);
            if (businessType is null)
                return Result.Failure<OpeningHoursDto>("Negocio no encontrado.");

            settings = new TenantSettings(tenantId.Value, TenantCapabilityDefaults.FromBusinessType(businessType.Value));
            _context.TenantSettings.Add(settings);
        }

        var previous = settings.OpeningHours;
        settings.SetOpeningHours(hours);
        if (previous != hours)
        {
            // The hours decide when customers can order online: who changed them, and from what, stays recorded.
            _context.AuditLogs.Add(new AuditLog(
                tenantId: tenantId.Value,
                entityName: nameof(TenantSettings),
                entityId: tenantId.Value.ToString(),
                action: "OpeningHoursUpdated",
                performedBy: _currentUserService.UserName ?? "system",
                metadata: JsonSerializer.Serialize(new
                {
                    Previous = previous is null ? null : OpeningHoursText.Describe(previous),
                    New = hours is null ? null : OpeningHoursText.Describe(hours)
                })));
        }

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(new OpeningHoursDto { Intervals = hours?.ToDtos() ?? new() });
    }
}
