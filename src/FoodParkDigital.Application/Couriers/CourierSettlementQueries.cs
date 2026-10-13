using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.HomeDelivery;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Couriers;

/// <summary>What each courier owes and carries (Fase 22D): the card of Caja › Repartidores. Whoever handles the cash.</summary>
[RequiresPermission(TenantPermission.ManageCash)]
public record GetCourierReceivablesQuery : IRequest<IReadOnlyList<CourierReceivableDto>>;

public class GetCourierReceivablesQueryHandler : IRequestHandler<GetCourierReceivablesQuery, IReadOnlyList<CourierReceivableDto>>
{
    private readonly IApplicationDbContext _context;

    public GetCourierReceivablesQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<CourierReceivableDto>> Handle(GetCourierReceivablesQuery request, CancellationToken cancellationToken)
    {
        var receivables = new List<CourierReceivableDto>();
        foreach (var courier in Enum.GetValues<DeliveryCourier>())
        {
            var owed = await CourierSettlementReader.Owed(_context, courier)
                .AsNoTracking()
                .GroupBy(_ => 1)
                .Select(g => new { Count = g.Count(), Amount = g.Sum(o => o.Total - o.PaidAmount), Oldest = g.Min(o => o.Delivery!.DispatchedAt) })
                .FirstOrDefaultAsync(cancellationToken);
            var onTheWay = await CourierSettlementReader.OnTheWay(_context, courier)
                .AsNoTracking()
                .GroupBy(_ => 1)
                .Select(g => new { Count = g.Count(), Amount = g.Sum(o => o.Delivery!.CashToCollect!.Value) })
                .FirstOrDefaultAsync(cancellationToken);
            var lastSettledAt = await _context.CourierSettlements
                .AsNoTracking()
                .Where(s => s.Courier == courier)
                .MaxAsync(s => (DateTimeOffset?)s.SettledAt, cancellationToken);

            receivables.Add(new CourierReceivableDto
            {
                Courier = courier,
                OwedOrders = owed?.Count ?? 0,
                OwedAmount = owed?.Amount ?? 0m,
                OldestDispatchedAt = owed?.Oldest,
                OnTheWayOrders = onTheWay?.Count ?? 0,
                OnTheWayAmount = onTheWay?.Amount ?? 0m,
                LastSettledAt = lastSettledAt
            });
        }

        return receivables;
    }
}

/// <summary>The orders a courier owes, the oldest first and as many as one settlement takes, and those on their way.</summary>
[RequiresPermission(TenantPermission.ManageCash)]
public record GetPendingCourierOrdersQuery(DeliveryCourier Courier) : IRequest<PendingCourierOrdersDto>;

public class GetPendingCourierOrdersQueryValidator : AbstractValidator<GetPendingCourierOrdersQuery>
{
    public GetPendingCourierOrdersQueryValidator()
    {
        RuleFor(x => x.Courier).IsInEnum().WithMessage(CourierSettlement.CourierMessage);
    }
}

public class GetPendingCourierOrdersQueryHandler : IRequestHandler<GetPendingCourierOrdersQuery, PendingCourierOrdersDto>
{
    private readonly IApplicationDbContext _context;

    public GetPendingCourierOrdersQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PendingCourierOrdersDto> Handle(GetPendingCourierOrdersQuery request, CancellationToken cancellationToken)
    {
        var owed = await CourierSettlementReader.AsCourierOrders(CourierSettlementReader.Owed(_context, request.Courier).AsNoTracking())
            .OrderBy(o => o.DispatchedAt)
            .ThenBy(o => o.OrderNumber)
            .Take(CourierSettlement.MaxOrders + 1)
            .ToListAsync(cancellationToken);
        var onTheWay = await CourierSettlementReader.AsCourierOrders(CourierSettlementReader.OnTheWay(_context, request.Courier).AsNoTracking())
            .OrderBy(o => o.DispatchedAt)
            .ThenBy(o => o.OrderNumber)
            .Take(CourierSettlement.MaxOrders)
            .ToListAsync(cancellationToken);

        return new PendingCourierOrdersDto
        {
            Owed = owed.Take(CourierSettlement.MaxOrders).ToList(),
            OnTheWay = onTheWay,
            Truncated = owed.Count > CourierSettlement.MaxOrders
        };
    }
}

/// <summary>The settlements of the couriers, the newest first.</summary>
[RequiresPermission(TenantPermission.ManageCash)]
public record GetCourierSettlementsQuery : IRequest<PaginatedList<CourierSettlementDto>>
{
    public DeliveryCourier? Courier { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public class GetCourierSettlementsQueryValidator : AbstractValidator<GetCourierSettlementsQuery>
{
    public GetCourierSettlementsQueryValidator()
    {
        RuleFor(x => x.Courier).IsInEnum().When(x => x.Courier is not null).WithMessage(CourierSettlement.CourierMessage);
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public class GetCourierSettlementsQueryHandler : IRequestHandler<GetCourierSettlementsQuery, PaginatedList<CourierSettlementDto>>
{
    private readonly IApplicationDbContext _context;

    public GetCourierSettlementsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public Task<PaginatedList<CourierSettlementDto>> Handle(GetCourierSettlementsQuery request, CancellationToken cancellationToken)
    {
        var settlements = _context.CourierSettlements.AsNoTracking();
        if (request.Courier is { } courier)
            settlements = settlements.Where(s => s.Courier == courier);

        var rows = settlements
            .OrderByDescending(s => s.SettledAt)
            .ThenBy(s => s.Id)
            .Select(CourierSettlementReader.AsDto());
        return PaginatedList<CourierSettlementDto>.CreateAsync(rows, request.PageNumber, request.PageSize, cancellationToken);
    }
}

/// <summary>A settlement with the orders it paid and its adjustments.</summary>
[RequiresPermission(TenantPermission.ManageCash)]
public record GetCourierSettlementQuery(Guid Id) : IRequest<CourierSettlementDetailDto>;

public class GetCourierSettlementQueryHandler : IRequestHandler<GetCourierSettlementQuery, CourierSettlementDetailDto>
{
    private readonly IApplicationDbContext _context;

    public GetCourierSettlementQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public Task<CourierSettlementDetailDto> Handle(GetCourierSettlementQuery request, CancellationToken cancellationToken)
        => CourierSettlementReader.DetailAsync(_context, request.Id, cancellationToken);
}
