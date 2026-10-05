using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Billing.Entities;
using FoodParkDigital.Domain.Billing.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Billing;

// Platform-wide reads for the platform admin (RequiresPlatformAdmin): subscriptions, branches, members and orders
// are tenant-scoped, so these queries ignore the tenant filter on purpose and never write.

public class PlatformOverviewDto
{
    public int TotalTenants { get; set; }
    public int ActiveTenants { get; set; }
    /// <summary>Suspended or cancelled: without access.</summary>
    public int TenantsWithoutAccess { get; set; }
    public int NewTenantsLast30Days { get; set; }
    public int WithoutPlan { get; set; }
    public int InTrial { get; set; }
    public int Paying { get; set; }
    public int PastDue { get; set; }
    public int Suspended { get; set; }
    public int Cancelled { get; set; }
    /// <summary>Monthly price of the plans of the businesses paid up to date.</summary>
    public decimal MonthlyRecurringRevenue { get; set; }
    public decimal PaymentsLast30Days { get; set; }
    public int TrialsEndingIn7Days { get; set; }
}

[RequiresPlatformAdmin]
public record GetPlatformOverviewQuery : IRequest<PlatformOverviewDto>;

public class GetPlatformOverviewQueryHandler : IRequestHandler<GetPlatformOverviewQuery, PlatformOverviewDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _clock;

    public GetPlatformOverviewQueryHandler(IApplicationDbContext context, IDateTimeProvider clock)
    {
        _context = context;
        _clock = clock;
    }

    public async Task<PlatformOverviewDto> Handle(GetPlatformOverviewQuery request, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var monthAgo = now.AddDays(-30);
        var tenants = await _context.Tenants.AsNoTracking()
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var subscriptions = await _context.TenantSubscriptions.IgnoreQueryFilters().AsNoTracking()
            .Join(_context.Plans, s => s.PlanId, p => p.Id, (s, p) => new { s.Status, s.TrialEndsAt, p.MonthlyPrice })
            .ToListAsync(cancellationToken);
        var total = tenants.Sum(x => x.Count);

        return new PlatformOverviewDto
        {
            TotalTenants = total,
            ActiveTenants = tenants.Where(x => x.Status == TenantStatus.Active).Sum(x => x.Count),
            TenantsWithoutAccess = tenants.Where(x => x.Status != TenantStatus.Active).Sum(x => x.Count),
            NewTenantsLast30Days = await _context.Tenants.CountAsync(x => x.CreatedAt >= monthAgo, cancellationToken),
            WithoutPlan = total - subscriptions.Count,
            InTrial = subscriptions.Count(x => x.Status == SubscriptionStatus.Trial),
            Paying = subscriptions.Count(x => x.Status == SubscriptionStatus.Active),
            PastDue = subscriptions.Count(x => x.Status == SubscriptionStatus.PastDue),
            Suspended = subscriptions.Count(x => x.Status == SubscriptionStatus.Suspended),
            Cancelled = subscriptions.Count(x => x.Status == SubscriptionStatus.Cancelled),
            MonthlyRecurringRevenue = subscriptions.Where(x => x.Status == SubscriptionStatus.Active).Sum(x => x.MonthlyPrice),
            PaymentsLast30Days = await _context.SubscriptionPayments.IgnoreQueryFilters()
                .Where(x => x.PaidAt >= monthAgo)
                .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m,
            TrialsEndingIn7Days = subscriptions.Count(x => x.Status == SubscriptionStatus.Trial && x.TrialEndsAt <= now.AddDays(7))
        };
    }
}

public class PlatformTenantDto
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public BusinessType BusinessType { get; set; }
    public TenantStatus TenantStatus { get; set; }
    public string? ContactEmail { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? PlanId { get; set; }
    public string? PlanName { get; set; }
    public SubscriptionStatus? SubscriptionStatus { get; set; }
    public DateTimeOffset? TrialEndsAt { get; set; }
    public DateTimeOffset? PaidThrough { get; set; }
    public string? StatusReason { get; set; }
    public int BranchesCount { get; set; }
    public int UsersCount { get; set; }
    public DateTimeOffset? LastOrderAt { get; set; }
}

/// <summary>The businesses of the platform with their plan and billing status, the newest first.</summary>
[RequiresPlatformAdmin]
public record GetPlatformTenantsQuery : IRequest<PaginatedList<PlatformTenantDto>>
{
    public string? Search { get; init; }
    public SubscriptionStatus? SubscriptionStatus { get; init; }
    public bool WithoutPlan { get; init; }
    public Guid? PlanId { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public class GetPlatformTenantsQueryValidator : AbstractValidator<GetPlatformTenantsQuery>
{
    public GetPlatformTenantsQueryValidator()
    {
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public class GetPlatformTenantsQueryHandler : IRequestHandler<GetPlatformTenantsQuery, PaginatedList<PlatformTenantDto>>
{
    private readonly IApplicationDbContext _context;

    public GetPlatformTenantsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PaginatedList<PlatformTenantDto>> Handle(GetPlatformTenantsQuery request, CancellationToken cancellationToken)
    {
        var subscriptions = _context.TenantSubscriptions.IgnoreQueryFilters();
        var query =
            from tenant in _context.Tenants.AsNoTracking()
            join subscription in subscriptions on tenant.Id equals subscription.TenantId into subscriptionJoin
            from subscription in subscriptionJoin.DefaultIfEmpty()
            join plan in _context.Plans on subscription.PlanId equals plan.Id into planJoin
            from plan in planJoin.DefaultIfEmpty()
            select new { tenant, subscription, plan };

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            query = query.Where(x => x.tenant.Name.Contains(search) || x.tenant.Slug.Contains(search)
                || (x.tenant.ContactEmail != null && x.tenant.ContactEmail.Contains(search)));
        }
        if (request.WithoutPlan)
            query = query.Where(x => x.subscription == null);
        if (request.SubscriptionStatus is { } status)
            query = query.Where(x => x.subscription != null && x.subscription.Status == status);
        if (request.PlanId is { } planId)
            query = query.Where(x => x.subscription != null && x.subscription.PlanId == planId);

        var rows = query
            .OrderByDescending(x => x.tenant.CreatedAt)
            .Select(x => new PlatformTenantDto
            {
                TenantId = x.tenant.Id,
                Name = x.tenant.Name,
                Slug = x.tenant.Slug,
                BusinessType = x.tenant.BusinessType,
                TenantStatus = x.tenant.Status,
                ContactEmail = x.tenant.ContactEmail,
                CreatedAt = x.tenant.CreatedAt,
                PlanId = x.subscription != null ? x.subscription.PlanId : null,
                PlanName = x.plan != null ? x.plan.Name : null,
                SubscriptionStatus = x.subscription != null ? x.subscription.Status : null,
                TrialEndsAt = x.subscription != null ? x.subscription.TrialEndsAt : null,
                PaidThrough = x.subscription != null ? x.subscription.PaidThrough : null,
                StatusReason = x.subscription != null ? x.subscription.StatusReason : null,
                BranchesCount = _context.Branches.IgnoreQueryFilters().Count(b => b.TenantId == x.tenant.Id && b.IsActive),
                UsersCount = _context.TenantUsers.IgnoreQueryFilters().Count(u => u.TenantId == x.tenant.Id && u.Status != TenantUserStatus.Disabled),
                LastOrderAt = _context.Orders.IgnoreQueryFilters()
                    .Where(o => o.TenantId == x.tenant.Id)
                    .Max(o => (DateTimeOffset?)o.CreatedAt)
            });

        return await PaginatedList<PlatformTenantDto>.CreateAsync(rows, request.PageNumber, request.PageSize, cancellationToken);
    }
}

public class SubscriptionPaymentDto
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = Plan.DefaultCurrency;
    public int Months { get; set; }
    public SubscriptionPaymentMethod Method { get; set; }
    public string? Reference { get; set; }
    public DateTimeOffset PaidAt { get; set; }
    public DateTimeOffset PeriodStart { get; set; }
    public DateTimeOffset PeriodEnd { get; set; }
    public string RecordedBy { get; set; } = null!;
}

/// <summary>A business's plan, billing status, limits in use and payments.</summary>
public class SubscriptionDetailDto
{
    public bool HasPlan { get; set; }
    public PlanDto? Plan { get; set; }
    public SubscriptionStatus? Status { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? TrialEndsAt { get; set; }
    public DateTimeOffset? PaidThrough { get; set; }
    /// <summary>When the business loses access if nothing is paid.</summary>
    public DateTimeOffset? SuspendsAt { get; set; }
    public string? StatusReason { get; set; }
    public bool SuspendedByPlatform { get; set; }
    public int BranchesUsed { get; set; }
    public int UsersUsed { get; set; }
    public List<SubscriptionPaymentDto> Payments { get; set; } = [];

    internal static async Task<SubscriptionDetailDto> LoadAsync(
        IApplicationDbContext context, Guid tenantId, IDateTimeProvider clock, int maxPayments, CancellationToken cancellationToken)
    {
        var subscription = await context.TenantSubscriptions.AsNoTracking()
            .Include(x => x.Payments)
            .FirstOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);
        var detail = new SubscriptionDetailDto
        {
            BranchesUsed = await context.Branches.CountAsync(x => x.TenantId == tenantId && x.IsActive, cancellationToken),
            UsersUsed = await context.TenantUsers.CountAsync(x => x.TenantId == tenantId && x.Status != TenantUserStatus.Disabled, cancellationToken)
        };
        if (subscription is null)
            return detail;

        var plan = await context.Plans.AsNoTracking().FirstAsync(x => x.Id == subscription.PlanId, cancellationToken);
        detail.HasPlan = true;
        detail.Plan = PlanFieldRules.ToDto(plan, 0);
        // Shown as the dates give it now, even if the hourly refresh has not stored it yet.
        detail.Status = subscription.Evaluate(clock.UtcNow);
        detail.StartedAt = subscription.StartedAt;
        detail.TrialEndsAt = subscription.TrialEndsAt;
        detail.PaidThrough = subscription.PaidThrough;
        detail.SuspendsAt = subscription.Status == SubscriptionStatus.Cancelled ? null : subscription.SuspendsAt;
        detail.StatusReason = subscription.StatusReason;
        detail.SuspendedByPlatform = subscription.SuspendedByPlatform;
        detail.Payments = subscription.Payments
            .OrderByDescending(x => x.PaidAt)
            .Take(maxPayments)
            .Select(x => new SubscriptionPaymentDto
            {
                Id = x.Id,
                Amount = x.Amount,
                Currency = x.Currency,
                Months = x.Months,
                Method = x.Method,
                Reference = x.Reference,
                PaidAt = x.PaidAt,
                PeriodStart = x.PeriodStart,
                PeriodEnd = x.PeriodEnd,
                RecordedBy = x.RecordedBy
            })
            .ToList();
        return detail;
    }
}

/// <summary>Billing of one business, for the platform console.</summary>
[RequiresPlatformAdmin]
public record GetTenantSubscriptionQuery : IRequest<SubscriptionDetailDto?>
{
    public Guid TenantId { get; init; }
}

public class GetTenantSubscriptionQueryHandler : IRequestHandler<GetTenantSubscriptionQuery, SubscriptionDetailDto?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IDateTimeProvider _clock;

    public GetTenantSubscriptionQueryHandler(IApplicationDbContext context, ICurrentTenantService tenantService, IDateTimeProvider clock)
    {
        _context = context;
        _tenantService = tenantService;
        _clock = clock;
    }

    public async Task<SubscriptionDetailDto?> Handle(GetTenantSubscriptionQuery request, CancellationToken cancellationToken)
    {
        if (!await _context.Tenants.AnyAsync(x => x.Id == request.TenantId, cancellationToken))
            return null;
        // The platform admin is not a member: the business asked for is put in scope for its own rows.
        _tenantService.SetTenant(request.TenantId);
        return await SubscriptionDetailDto.LoadAsync(_context, request.TenantId, _clock, maxPayments: 50, cancellationToken);
    }
}

/// <summary>The plan of the current business, for its owners and admins (Mi negocio).</summary>
[RequiresTenantManager]
public record GetMySubscriptionQuery : IRequest<SubscriptionDetailDto>;

public class GetMySubscriptionQueryHandler : IRequestHandler<GetMySubscriptionQuery, SubscriptionDetailDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IDateTimeProvider _clock;

    public GetMySubscriptionQueryHandler(IApplicationDbContext context, ICurrentTenantService tenantService, IDateTimeProvider clock)
    {
        _context = context;
        _tenantService = tenantService;
        _clock = clock;
    }

    public Task<SubscriptionDetailDto> Handle(GetMySubscriptionQuery request, CancellationToken cancellationToken)
        => SubscriptionDetailDto.LoadAsync(_context, _tenantService.TenantId, _clock, maxPayments: 12, cancellationToken);
}
