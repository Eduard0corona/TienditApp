using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Finance.DTOs;
using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Finance.Queries;

/// <summary>
/// The drawer the current user's payments enter, by the same rule as a payment (<see cref="OrderCashService.PickDrawer"/>):
/// their own open session or, while it is the only one there, the open drawer of the branch where they sell.
/// Null when a payment would be refused for lack of a drawer.
/// </summary>
[RequiresPermission(TenantPermission.ManageCash)]
public record GetCurrentCashSessionQuery : IRequest<CashSessionDto?>
{
    /// <summary>Branch where the user sells; without it, the only open drawer of the business stands in for theirs.</summary>
    public Guid? BranchId { get; init; }
}

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
        var openSessions = await _context.CashSessions
            .AsNoTracking()
            .Where(x => x.Status == CashSessionStatus.Open)
            .Select(x => new OrderCashService.DrawerCandidate(x.Id, x.OpenedByUserId, x.BranchId, x.InventoryLocationId))
            .ToListAsync(cancellationToken);
        var sessionId = OrderCashService.PickDrawer(openSessions, userId, request.BranchId, inventoryLocationId: null);
        if (sessionId is null)
            return null;

        var row = await _context.CashSessions
            .AsNoTracking()
            .Where(x => x.Id == sessionId.Value)
            .Select(CashSessionProjection.ToRow(_context))
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : CashSessionProjection.ToDto(row, userId);
    }
}
