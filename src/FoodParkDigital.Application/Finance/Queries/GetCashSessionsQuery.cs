using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Finance.DTOs;
using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Finance.Queries;

/// <summary>Shift history: who opened each drawer, where, what it should have had and what was counted.</summary>
[RequiresPermission(TenantPermission.ManageCash)]
public record GetCashSessionsQuery : IRequest<PaginatedList<CashSessionDto>>
{
    public DateTimeOffset? FromDate { get; init; }
    public DateTimeOffset? ToDate { get; init; }
    public Guid? InventoryLocationId { get; init; }
    public CashSessionStatus? Status { get; init; }

    /// <summary>Only drawers with a shortage or surplus at closing.</summary>
    public bool OnlyWithDifference { get; init; }

    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public class GetCashSessionsQueryValidator : AbstractValidator<GetCashSessionsQuery>
{
    public GetCashSessionsQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
        RuleFor(x => x.Status).IsInEnum().When(x => x.Status.HasValue);
    }
}

public class GetCashSessionsQueryHandler : IRequestHandler<GetCashSessionsQuery, PaginatedList<CashSessionDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public GetCashSessionsQueryHandler(IApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<PaginatedList<CashSessionDto>> Handle(GetCashSessionsQuery request, CancellationToken cancellationToken)
    {
        var query = _context.CashSessions.AsNoTracking();

        if (request.FromDate is { } from)
            query = query.Where(x => x.OpenedAt >= from);
        if (request.ToDate is { } to)
            query = query.Where(x => x.OpenedAt <= to);
        if (request.InventoryLocationId is { } locationId)
            query = query.Where(x => x.InventoryLocationId == locationId);
        if (request.Status is { } status)
            query = query.Where(x => x.Status == status);
        if (request.OnlyWithDifference)
            query = query.Where(x => x.ClosingDifference != null && x.ClosingDifference != 0);

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(x => x.OpenedAt)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(CashSessionProjection.ToRow(_context))
            .ToListAsync(cancellationToken);

        var userId = CashSessionMapping.ParseUserId(_currentUserService.UserId);
        return new PaginatedList<CashSessionDto>(
            rows.Select(row => CashSessionProjection.ToDto(row, userId)).ToList(),
            totalCount,
            request.PageNumber,
            request.PageSize);
    }
}
