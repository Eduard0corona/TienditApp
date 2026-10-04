using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Billing.Entities;
using FoodParkDigital.Domain.Billing.Enums;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Billing;

public class PlanDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public decimal MonthlyPrice { get; set; }
    public string Currency { get; set; } = Plan.DefaultCurrency;
    public TenantCapability Modules { get; set; }
    public int MaxBranches { get; set; }
    public int MaxUsers { get; set; }
    public int TrialDays { get; set; }
    public bool IsDefaultForNewBusinesses { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
    /// <summary>Businesses on the plan (cancelled ones aside).</summary>
    public int TenantsCount { get; set; }
}

/// <summary>Fields of a plan as the platform console sends them.</summary>
public abstract record PlanFields
{
    public string Code { get; init; } = null!;
    public string Name { get; init; } = null!;
    public string? Description { get; init; }
    public decimal MonthlyPrice { get; init; }
    public TenantCapability Modules { get; init; }
    public int MaxBranches { get; init; } = 1;
    public int MaxUsers { get; init; } = 1;
    public int TrialDays { get; init; }
    public int SortOrder { get; init; }
    /// <summary>Businesses that sign up on their own start a trial on this plan.</summary>
    public bool IsDefaultForNewBusinesses { get; init; }
}

internal static class PlanFieldRules
{
    public static void Apply<T>(AbstractValidator<T> validator) where T : PlanFields
    {
        validator.RuleFor(x => x.Code).NotEmpty().MaximumLength(Plan.MaxCodeLength);
        validator.RuleFor(x => x.Name).NotEmpty().MaximumLength(Plan.MaxNameLength);
        validator.RuleFor(x => x.Description).MaximumLength(Plan.MaxDescriptionLength);
        validator.RuleFor(x => x.MonthlyPrice).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, ignoreTrailingZeros: true);
        validator.RuleFor(x => x.MaxBranches).InclusiveBetween(1, Plan.MaxBranchesLimit);
        validator.RuleFor(x => x.MaxUsers).InclusiveBetween(1, Plan.MaxUsersLimit);
        validator.RuleFor(x => x.TrialDays).InclusiveBetween(0, Plan.MaxTrialDays);
    }

    /// <summary>
    /// Moves the "new businesses" mark in two saves inside the caller's transaction: the database allows one such
    /// plan, and a single save may write the new mark before clearing the old one.
    /// </summary>
    public static async Task MoveDefaultAsync(IApplicationDbContext context, Plan plan, CancellationToken cancellationToken)
    {
        var previous = await context.Plans
            .Where(x => x.IsDefaultForNewBusinesses && x.Id != plan.Id)
            .ToListAsync(cancellationToken);
        previous.ForEach(x => x.UnmarkAsDefaultForNewBusinesses());
        await context.SaveChangesAsync(cancellationToken);

        plan.MarkAsDefaultForNewBusinesses();
        await context.SaveChangesAsync(cancellationToken);
    }

    public static PlanDto ToDto(Plan plan, int tenantsCount) => new()
    {
        Id = plan.Id,
        Code = plan.Code,
        Name = plan.Name,
        Description = plan.Description,
        MonthlyPrice = plan.MonthlyPrice,
        Currency = plan.Currency,
        Modules = plan.Modules,
        MaxBranches = plan.MaxBranches,
        MaxUsers = plan.MaxUsers,
        TrialDays = plan.TrialDays,
        IsDefaultForNewBusinesses = plan.IsDefaultForNewBusinesses,
        IsActive = plan.IsActive,
        SortOrder = plan.SortOrder,
        TenantsCount = tenantsCount
    };
}

/// <summary>Plans of the platform with how many businesses are on each.</summary>
[RequiresPlatformAdmin]
public record GetPlansQuery : IRequest<List<PlanDto>>;

public class GetPlansQueryHandler : IRequestHandler<GetPlansQuery, List<PlanDto>>
{
    private readonly IApplicationDbContext _context;

    public GetPlansQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<PlanDto>> Handle(GetPlansQuery request, CancellationToken cancellationToken)
    {
        var plans = await _context.Plans.AsNoTracking()
            .OrderBy(x => x.SortOrder).ThenBy(x => x.MonthlyPrice).ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);
        // Platform-wide count: subscriptions are tenant-scoped and this request has no business.
        var counts = await _context.TenantSubscriptions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.Status != SubscriptionStatus.Cancelled)
            .GroupBy(x => x.PlanId)
            .Select(g => new { PlanId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PlanId, x => x.Count, cancellationToken);
        return plans.Select(p => PlanFieldRules.ToDto(p, counts.GetValueOrDefault(p.Id))).ToList();
    }
}

[RequiresPlatformAdmin]
public record CreatePlanCommand : PlanFields, IRequest<Result<PlanDto>>;

public class CreatePlanCommandValidator : AbstractValidator<CreatePlanCommand>
{
    public CreatePlanCommandValidator() => PlanFieldRules.Apply(this);
}

public class CreatePlanCommandHandler : IRequestHandler<CreatePlanCommand, Result<PlanDto>>
{
    private readonly IApplicationDbContext _context;

    public CreatePlanCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public Task<Result<PlanDto>> Handle(CreatePlanCommand request, CancellationToken cancellationToken)
        => _context.ExecuteInResilientTransactionAsync(async ct =>
        {
            var code = Plan.NormalizeCode(request.Code);
            if (await _context.Plans.AnyAsync(x => x.Code == code, ct))
                return Result.Failure<PlanDto>("Ya existe un plan con esa clave.");

            Plan plan;
            try
            {
                plan = new Plan(request.Code, request.Name, request.Description, request.MonthlyPrice, request.Modules,
                    request.MaxBranches, request.MaxUsers, request.TrialDays, request.SortOrder);
            }
            catch (DomainException ex)
            {
                return Result.Failure<PlanDto>(ex.Message);
            }

            _context.Plans.Add(plan);
            await _context.SaveChangesAsync(ct);
            if (request.IsDefaultForNewBusinesses)
                await PlanFieldRules.MoveDefaultAsync(_context, plan, ct);

            return Result.Success(PlanFieldRules.ToDto(plan, 0));
        }, cancellationToken);
}

/// <summary>
/// Changes a plan. Businesses on it keep only the modules it still includes (their configuration is trimmed), and
/// an inactive plan is no longer offered while businesses already on it keep it.
/// </summary>
[RequiresPlatformAdmin]
public record UpdatePlanCommand : PlanFields, IRequest<Result<PlanDto>>
{
    public Guid Id { get; init; }
    public bool IsActive { get; init; } = true;
}

public class UpdatePlanCommandValidator : AbstractValidator<UpdatePlanCommand>
{
    public UpdatePlanCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        PlanFieldRules.Apply(this);
    }
}

public class UpdatePlanCommandHandler : IRequestHandler<UpdatePlanCommand, Result<PlanDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly ICurrentTenantService _tenantService;

    public UpdatePlanCommandHandler(IApplicationDbContext context, ICurrentUserService currentUser, ICurrentTenantService tenantService)
    {
        _context = context;
        _currentUser = currentUser;
        _tenantService = tenantService;
    }

    public Task<Result<PlanDto>> Handle(UpdatePlanCommand request, CancellationToken cancellationToken)
    {
        // A platform change touches several businesses: no business is in scope, every query names its tenant.
        _tenantService.SetTenant(Guid.Empty);
        return _context.ExecuteInResilientTransactionAsync(async ct =>
        {
            var plan = await _context.Plans.FirstOrDefaultAsync(x => x.Id == request.Id, ct);
            if (plan is null)
                return Result.Failure<PlanDto>("El plan no existe.");
            var code = Plan.NormalizeCode(request.Code);
            if (await _context.Plans.AnyAsync(x => x.Code == code && x.Id != plan.Id, ct))
                return Result.Failure<PlanDto>("Ya existe un plan con esa clave.");

            var removedModules = plan.Modules & ~request.Modules;
            try
            {
                plan.Update(request.Code, request.Name, request.Description, request.MonthlyPrice, request.Modules,
                    request.MaxBranches, request.MaxUsers, request.TrialDays, request.SortOrder);
                if (!request.IsDefaultForNewBusinesses && plan.IsDefaultForNewBusinesses)
                    plan.UnmarkAsDefaultForNewBusinesses();
                if (request.IsActive)
                    plan.Activate();
                else
                    plan.Deactivate();
            }
            catch (DomainException ex)
            {
                return Result.Failure<PlanDto>(ex.Message);
            }

            await _context.SaveChangesAsync(ct);
            if (request.IsDefaultForNewBusinesses && !plan.IsDefaultForNewBusinesses)
            {
                if (!plan.IsActive)
                    return Result.Failure<PlanDto>("Activa el plan antes de ofrecerlo a los negocios nuevos.");
                await PlanFieldRules.MoveDefaultAsync(_context, plan, ct);
            }

            if (removedModules != TenantCapability.None)
                await TrimBusinessesAsync(plan, removedModules, ct);

            var tenantsCount = await _context.TenantSubscriptions.IgnoreQueryFilters()
                .CountAsync(x => x.PlanId == plan.Id && x.Status != SubscriptionStatus.Cancelled, ct);
            return Result.Success(PlanFieldRules.ToDto(plan, tenantsCount));
        }, cancellationToken);
    }

    /// <summary>The plan no longer includes some modules: businesses on it turn them off.</summary>
    private async Task TrimBusinessesAsync(Plan plan, TenantCapability removedModules, CancellationToken cancellationToken)
    {
        // Platform-wide change made by the platform admin: every business on the plan, each one audited.
        var tenants = await _context.TenantSubscriptions.IgnoreQueryFilters()
            .Where(x => x.PlanId == plan.Id && x.Status != SubscriptionStatus.Cancelled)
            .Join(_context.TenantSettings.IgnoreQueryFilters(), s => s.TenantId, t => t.TenantId, (s, t) => t)
            .Join(_context.Tenants.IgnoreQueryFilters(), t => t.TenantId, x => x.Id, (settings, tenant) => new { settings, tenant.BusinessType })
            .ToListAsync(cancellationToken);
        foreach (var item in tenants)
        {
            var before = item.settings.Capabilities;
            SubscriptionRules.ClampModules(item.settings, item.BusinessType, plan);
            if (before != item.settings.Capabilities)
            {
                _context.AuditLogs.Add(SubscriptionRules.Audit(item.settings.TenantId, "PlanModulesTrimmed", _currentUser.UserName ?? "system", new
                {
                    Plan = plan.Code,
                    Removed = removedModules.ToString(),
                    Previous = before.ToString(),
                    Current = item.settings.Capabilities.ToString()
                }));
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
