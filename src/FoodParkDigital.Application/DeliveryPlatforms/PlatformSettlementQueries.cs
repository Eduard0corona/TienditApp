using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Reports.ReadModels;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.DeliveryPlatforms;

/// <summary>The settlements of the business (Reportes › Liquidaciones), the newest period first.</summary>
[RequiresPermission(TenantPermission.ViewReports)]
public record GetPlatformSettlementsQuery : IRequest<PaginatedList<PlatformSettlementDto>>
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>Only those of one platform.</summary>
    public OrderChannel? Channel { get; init; }

    /// <summary>Only open, closed or voided ones.</summary>
    public PlatformSettlementStatus? Status { get; init; }

    /// <summary>Only those whose reference or notes have this text (the folio of the transfer, for instance).</summary>
    public string? Search { get; init; }

    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = DefaultPageSize;
}

public class GetPlatformSettlementsQueryValidator : AbstractValidator<GetPlatformSettlementsQuery>
{
    public GetPlatformSettlementsQueryValidator()
    {
        RuleFor(x => x.Channel)
            .Must(channel => channel is null || OrderChannelRules.IsDeliveryPlatform(channel.Value))
            .WithMessage(DeliveryPlatformSetting.NotADeliveryPlatformMessage);
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, GetPlatformSettlementsQuery.MaxPageSize);
    }
}

public class GetPlatformSettlementsQueryHandler : IRequestHandler<GetPlatformSettlementsQuery, PaginatedList<PlatformSettlementDto>>
{
    private readonly IApplicationDbContext _context;

    public GetPlatformSettlementsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PaginatedList<PlatformSettlementDto>> Handle(GetPlatformSettlementsQuery request, CancellationToken cancellationToken)
    {
        var settlements = _context.PlatformSettlements.AsNoTracking();
        if (request.Channel is { } channel)
            settlements = settlements.Where(s => s.Channel == channel);
        if (request.Status is { } status)
            settlements = settlements.Where(s => s.Status == status);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            // On the server, over every page; lower-cased on both sides, so it does not depend on the collation.
            var term = request.Search.Trim().ToLowerInvariant();
            settlements = settlements.Where(s =>
                (s.Reference != null && s.Reference.ToLower().Contains(term))
                || (s.Notes != null && s.Notes.ToLower().Contains(term)));
        }

        var rows = settlements
            .OrderByDescending(s => s.PeriodTo)
            .ThenByDescending(s => s.CreatedAt)
            .ThenBy(s => s.Id)
            .Select(s => new PlatformSettlementDto
            {
                Id = s.Id,
                Channel = s.Channel,
                Status = s.Status,
                PeriodFrom = s.PeriodFrom,
                PeriodTo = s.PeriodTo,
                DepositedOn = s.DepositedOn,
                DepositedAmount = s.DepositedAmount,
                Reference = s.Reference,
                Notes = s.Notes,
                OrdersTotal = s.OrdersTotal,
                AdjustmentsTotal = s.AdjustmentsTotal,
                ExpectedDeposit = s.OrdersTotal + s.AdjustmentsTotal,
                Difference = s.DepositedAmount - (s.OrdersTotal + s.AdjustmentsTotal),
                OrderCount = s.Orders.Count,
                Revision = s.Revision,
                CreatedAt = s.CreatedAt,
                ClosedAt = s.ClosedAt,
                VoidedAt = s.VoidedAt,
                VoidReason = s.VoidReason
            });

        return await PaginatedList<PlatformSettlementDto>.CreateAsync(rows, request.PageNumber, request.PageSize, cancellationToken);
    }
}

/// <summary>A settlement with its orders and adjustments and, while open, the pending orders it could add.</summary>
[RequiresPermission(TenantPermission.ViewReports)]
public record GetPlatformSettlementQuery(Guid Id) : IRequest<PlatformSettlementDetailDto>;

public class GetPlatformSettlementQueryHandler : IRequestHandler<GetPlatformSettlementQuery, PlatformSettlementDetailDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetPlatformSettlementQueryHandler(IApplicationDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<PlatformSettlementDetailDto> Handle(GetPlatformSettlementQuery request, CancellationToken cancellationToken)
    {
        var zone = await PlatformSettlementReader.ZoneAsync(_context, _tenantService.TenantId, cancellationToken);
        return await PlatformSettlementReader.DetailAsync(_context, request.Id, zone, cancellationToken);
    }
}

/// <summary>
/// The orders of a platform pending for a new settlement of a period: registered up to its last date (also the pending
/// ones of before, which a statement may bring late), the oldest first.
/// </summary>
[RequiresPermission(TenantPermission.ViewReports)]
public record GetPendingPlatformOrdersQuery : IRequest<PendingPlatformOrdersDto>
{
    public OrderChannel? Channel { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

public class GetPendingPlatformOrdersQueryValidator : AbstractValidator<GetPendingPlatformOrdersQuery>
{
    public const string ChooseThePeriodMessage = "Elige el periodo que paga el depósito.";

    public GetPendingPlatformOrdersQueryValidator()
    {
        RuleFor(x => x.Channel)
            .Must(channel => channel is { } value && OrderChannelRules.IsDeliveryPlatform(value))
            .WithMessage(DeliveryPlatformSetting.NotADeliveryPlatformMessage);
        RuleFor(x => x.From).NotNull().WithMessage(ChooseThePeriodMessage);
        RuleFor(x => x.To).NotNull().WithMessage(ChooseThePeriodMessage);
        RuleFor(x => x)
            .Must(x => x.From is null || x.To is null || x.To >= x.From)
            .WithMessage(PlatformSettlement.PeriodOrderMessage)
            .Must(x => x.From is null || x.To is null || x.To.Value.DayNumber - x.From.Value.DayNumber + 1 <= PlatformSettlement.MaxPeriodDays)
            .WithMessage(PlatformSettlement.PeriodTooLongMessage);
    }
}

public class GetPendingPlatformOrdersQueryHandler : IRequestHandler<GetPendingPlatformOrdersQuery, PendingPlatformOrdersDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetPendingPlatformOrdersQueryHandler(IApplicationDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<PendingPlatformOrdersDto> Handle(GetPendingPlatformOrdersQuery request, CancellationToken cancellationToken)
    {
        // The validator asks for all three; without them there is nothing to list.
        if (request.Channel is not { } channel || request.From is not { } from || request.To is not { } to)
            return new PendingPlatformOrdersDto();

        var zone = await PlatformSettlementReader.ZoneAsync(_context, _tenantService.TenantId, cancellationToken);
        var (orders, truncated) = await PlatformSettlementReader.CandidatesAsync(_context, channel, from, to, zone, [], cancellationToken);
        var openId = await _context.PlatformSettlements
            .AsNoTracking()
            .Where(s => s.Channel == channel && s.Status == PlatformSettlementStatus.Open)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return new PendingPlatformOrdersDto { Orders = orders, Truncated = truncated, OpenSettlementId = openId };
    }
}

/// <summary>
/// What each platform still owes for its orders not settled yet: those active, and any other with pending orders or an
/// open settlement, in the order of the platforms.
/// </summary>
[RequiresPermission(TenantPermission.ViewReports)]
public record GetPlatformReceivablesQuery : IRequest<List<PlatformReceivableDto>>;

public class GetPlatformReceivablesQueryHandler : IRequestHandler<GetPlatformReceivablesQuery, List<PlatformReceivableDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetPlatformReceivablesQueryHandler(IApplicationDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<List<PlatformReceivableDto>> Handle(GetPlatformReceivablesQuery request, CancellationToken cancellationToken)
    {
        var zone = await PlatformSettlementReader.ZoneAsync(_context, _tenantService.TenantId, cancellationToken);
        var active = await _context.DeliveryPlatformSettings
            .AsNoTracking()
            .Where(s => s.IsActive)
            .Select(s => s.Channel)
            .ToListAsync(cancellationToken);
        // Each pending order is counted as a settlement counts it, rounded one by one.
        var pending = (await PlatformSettlementReader.Pending(_context).ToListAsync(cancellationToken))
            .Where(row => row.ExpectedNet != 0)
            .GroupBy(row => row.Channel)
            .ToDictionary(group => group.Key, group => group.ToList());
        var open = await _context.PlatformSettlements
            .AsNoTracking()
            .Where(s => s.Status == PlatformSettlementStatus.Open)
            .Select(s => new { s.Channel, s.Id })
            .ToListAsync(cancellationToken);
        var settledThrough = await _context.PlatformSettlements
            .AsNoTracking()
            .Where(s => s.Status == PlatformSettlementStatus.Closed)
            .GroupBy(s => s.Channel)
            .Select(group => new { Channel = group.Key, Through = group.Max(s => s.PeriodTo) })
            .ToListAsync(cancellationToken);

        return OrderChannelRules.DeliveryPlatforms
            .Where(channel => active.Contains(channel) || pending.ContainsKey(channel) || open.Any(s => s.Channel == channel))
            .Select(channel =>
            {
                var orders = pending.GetValueOrDefault(channel) ?? [];
                return new PlatformReceivableDto
                {
                    Channel = channel,
                    IsActive = active.Contains(channel),
                    PendingOrders = orders.Count,
                    PendingAmount = orders.Sum(row => row.ExpectedNet),
                    OldestPendingOn = orders.Count == 0
                        ? null
                        : DailySalesReadModel.LocalDate(orders.Min(row => row.RegisteredAt), zone),
                    OpenSettlementId = open.FirstOrDefault(s => s.Channel == channel)?.Id,
                    SettledThrough = settledThrough.FirstOrDefault(s => s.Channel == channel)?.Through
                };
            })
            .ToList();
    }
}
