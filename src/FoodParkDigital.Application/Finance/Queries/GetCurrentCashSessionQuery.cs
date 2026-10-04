using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Finance.DTOs;
using FoodParkDigital.Domain.Finance.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Finance.Queries;

/// <summary>
/// The drawer the current user's cash payments enter: their own open session or, while it is the only one,
/// a drawer opened before per-cashier sessions.
/// </summary>
public record GetCurrentCashSessionQuery : IRequest<CashSessionDto?>;

public class GetCurrentCashSessionQueryHandler : IRequestHandler<GetCurrentCashSessionQuery, CashSessionDto?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService? _currentUserService;

    public GetCurrentCashSessionQueryHandler(IApplicationDbContext context, ICurrentUserService? currentUserService = null)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<CashSessionDto?> Handle(GetCurrentCashSessionQuery request, CancellationToken cancellationToken)
    {
        var userId = CashSessionMapping.ParseUserId(_currentUserService?.UserId);
        var open = _context.CashSessions
            .AsNoTracking()
            .Where(x => x.Status == CashSessionStatus.Open);

        var row = userId is null
            ? null
            : await open.Where(x => x.OpenedByUserId == userId)
                .Select(CashSessionProjection.ToRow(_context))
                .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            // Same fallback as the cash service: a single open drawer takes everybody's cash.
            var openRows = await open
                .OrderByDescending(x => x.OpenedAt)
                .Take(2)
                .Select(CashSessionProjection.ToRow(_context))
                .ToListAsync(cancellationToken);
            row = openRows.Count == 1 ? openRows[0] : null;
        }

        return row is null ? null : CashSessionProjection.ToDto(row, userId);
    }
}
