using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Audit;

/// <summary>
/// The platform admin opens an access to one business (Fase 32G, ADR 0099): why, and for how long (15 minutes to 4
/// hours). Until it ends, the <c>X-Tenant-Id</c> header acts on that business, and each request is recorded in its
/// activity with this reason. Opening it is recorded there too. One open access per business at a time.
/// </summary>
[RequiresPlatformAdmin]
public record StartSupportAccessCommand(Guid TenantId, string Reason, int Minutes) : IRequest<Result<SupportAccessDto>>;

/// <summary>The admin closes their open access to the business before its time. Recorded in its activity.</summary>
[RequiresPlatformAdmin]
public record EndSupportAccessCommand(Guid TenantId) : IRequest<Result>;

/// <summary>The admin's open accesses, the one closest to its end first.</summary>
[RequiresPlatformAdmin]
public record GetMySupportAccessesQuery : IRequest<IReadOnlyList<SupportAccessDto>>;

public record SupportAccessDto
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public string BusinessName { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}

public class StartSupportAccessCommandValidator : AbstractValidator<StartSupportAccessCommand>
{
    public StartSupportAccessCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.Reason)
            .Must(reason => reason?.Trim().Length is >= SupportAccess.MinReasonLength and <= SupportAccess.MaxReasonLength)
            .WithMessage(SupportAccess.ReasonMessage);
        RuleFor(x => x.Minutes)
            .InclusiveBetween(SupportAccess.MinMinutes, SupportAccess.MaxMinutes)
            .WithMessage(SupportAccess.MinutesMessage);
    }
}

public class EndSupportAccessCommandValidator : AbstractValidator<EndSupportAccessCommand>
{
    public EndSupportAccessCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
    }
}

public class SupportAccessHandlers :
    IRequestHandler<StartSupportAccessCommand, Result<SupportAccessDto>>,
    IRequestHandler<EndSupportAccessCommand, Result>,
    IRequestHandler<GetMySupportAccessesQuery, IReadOnlyList<SupportAccessDto>>
{
    public const string StartedAction = "SupportAccessStarted";
    public const string EndedAction = "SupportAccessEnded";
    public const string BusinessNotFoundMessage = "El negocio no existe.";
    public const string AlreadyOpenMessage = "Ya tienes un acceso abierto a este negocio: ciérralo para abrir otro.";
    public const string NoneOpenMessage = "No tienes un acceso abierto a este negocio.";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;

    public SupportAccessHandlers(
        IApplicationDbContext context, ICurrentTenantService tenantService, ICurrentUserService currentUser, IDateTimeProvider clock)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<SupportAccessDto>> Handle(StartSupportAccessCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUser.UserId, out var adminUserId))
            return Result.Failure<SupportAccessDto>(NoneOpenMessage);
        var business = await EnterBusinessAsync(request.TenantId, cancellationToken);
        if (business is null)
            return Result.Failure<SupportAccessDto>(BusinessNotFoundMessage);

        var now = _clock.UtcNow;
        if (await OpenAccessOf(business.Value.Id, adminUserId, now).AnyAsync(cancellationToken))
            return Result.Failure<SupportAccessDto>(AlreadyOpenMessage);

        SupportAccess access;
        try
        {
            access = SupportAccess.Start(business.Value.Id, adminUserId, request.Reason, request.Minutes, now);
        }
        catch (DomainException ex)
        {
            return Result.Failure<SupportAccessDto>(ex.Message);
        }

        _context.SupportAccesses.Add(access);
        _context.AuditLogs.Add(Audit(access, StartedAction, new
        {
            access.Reason,
            access.StartedAt,
            access.ExpiresAt,
            AdminUserId = adminUserId,
            SessionTenantId = _currentUser.TenantId
        }, now));
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(access, business.Value.Name));
    }

    public async Task<Result> Handle(EndSupportAccessCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUser.UserId, out var adminUserId))
            return Result.Failure(NoneOpenMessage);
        if (await EnterBusinessAsync(request.TenantId, cancellationToken) is not { } business)
            return Result.Failure(BusinessNotFoundMessage);

        var now = _clock.UtcNow;
        var access = await OpenAccessOf(business.Id, adminUserId, now).FirstOrDefaultAsync(cancellationToken);
        if (access is null || !access.End(now))
            return Result.Failure(NoneOpenMessage);

        _context.AuditLogs.Add(Audit(access, EndedAction, new
        {
            access.Reason,
            access.StartedAt,
            access.ExpiresAt,
            access.EndedAt,
            AdminUserId = adminUserId
        }, now));
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<IReadOnlyList<SupportAccessDto>> Handle(GetMySupportAccessesQuery request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUser.UserId, out var adminUserId))
            return [];
        var now = _clock.UtcNow;
        // The admin's own accesses, in every business they opened one: this list is theirs, not any business's.
        return await _context.SupportAccesses
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(a => a.AdminUserId == adminUserId && a.EndedAt == null && a.ExpiresAt > now)
            .Join(_context.Tenants, a => a.TenantId, t => t.Id, (a, t) => new SupportAccessDto
            {
                Id = a.Id,
                TenantId = a.TenantId,
                BusinessName = t.Name,
                Reason = a.Reason,
                StartedAt = a.StartedAt,
                ExpiresAt = a.ExpiresAt
            })
            .OrderBy(a => a.ExpiresAt)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Puts the business in scope, as the platform's other actions on one business: the access and its audit entry are
    /// written as that business's, and its accesses are read through the usual filter.
    /// </summary>
    private async Task<(Guid Id, string Name)?> EnterBusinessAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var business = await _context.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.Id, t.Name })
            .FirstOrDefaultAsync(cancellationToken);
        if (business is null)
            return null;
        _tenantService.SetTenant(business.Id);
        return (business.Id, business.Name);
    }

    /// <summary>The admin's open access to the business, once in scope.</summary>
    private IQueryable<SupportAccess> OpenAccessOf(Guid tenantId, Guid adminUserId, DateTimeOffset now)
        => _context.SupportAccesses
            .Where(a => a.TenantId == tenantId && a.AdminUserId == adminUserId && a.EndedAt == null && a.ExpiresAt > now)
            .OrderByDescending(a => a.ExpiresAt);

    private AuditLog Audit(SupportAccess access, string action, object details, DateTimeOffset now) => new(
        access.TenantId,
        nameof(SupportAccess),
        access.Id.ToString(),
        action,
        _currentUser.UserName ?? _currentUser.UserId ?? "platform",
        JsonSerializer.Serialize(details),
        now);

    private static SupportAccessDto ToDto(SupportAccess access, string businessName) => new()
    {
        Id = access.Id,
        TenantId = access.TenantId,
        BusinessName = businessName,
        Reason = access.Reason,
        StartedAt = access.StartedAt,
        ExpiresAt = access.ExpiresAt
    };
}
