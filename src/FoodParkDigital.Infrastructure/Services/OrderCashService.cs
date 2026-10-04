using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Services;

public class OrderCashService : IOrderCashService
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public OrderCashService(IApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<bool> RegisterOrderPaymentAsync(Order order, OrderPayment payment, CancellationToken cancellationToken)
    {
        var session = await FindDrawerAsync(order, payment.InventoryLocationId, cancellationToken);
        if (session is null)
            return false;

        session.RegisterTransaction(
            transactionType: CashTransactionType.SalePayment,
            amount: payment.Amount,
            occurredAt: payment.PaidAt,
            paymentMethod: payment.Method,
            inventoryLocationId: payment.InventoryLocationId,
            orderId: order.Id,
            reference: payment.Reference ?? order.OrderNumber,
            notes: payment.Notes);
        return true;
    }

    public async Task<bool> RegisterOrderRefundAsync(Order order, OrderRefund refund, CancellationToken cancellationToken)
    {
        var session = await FindDrawerAsync(order, refund.InventoryLocationId, cancellationToken);
        if (session is null)
            return false;

        // A refund leaves the drawer (negative); a non-cash refund is recorded apart from the expected cash.
        session.RegisterTransaction(
            transactionType: CashTransactionType.Refund,
            amount: refund.Amount,
            occurredAt: refund.RefundedAt,
            paymentMethod: refund.Method,
            inventoryLocationId: refund.InventoryLocationId,
            orderId: order.Id,
            reference: refund.Reference ?? order.OrderNumber,
            notes: refund.Reason);
        return true;
    }

    private async Task<Domain.Finance.Entities.CashSession?> FindDrawerAsync(
        Order order,
        Guid? inventoryLocationId,
        CancellationToken cancellationToken)
    {
        var openSessions = await _context.CashSessions
            .AsNoTracking()
            .Where(x => x.TenantId == order.TenantId && x.Status == CashSessionStatus.Open)
            .Select(x => new DrawerCandidate(x.Id, x.OpenedByUserId, x.BranchId, x.InventoryLocationId))
            .ToListAsync(cancellationToken);
        if (openSessions.Count == 0)
            return null;

        var userId = Guid.TryParse(_currentUserService.UserId, out var parsed) ? parsed : (Guid?)null;
        var sessionId = PickDrawer(openSessions, userId, order.BranchId, inventoryLocationId);
        if (sessionId is null)
            return null;

        return await _context.CashSessions
            .Include(x => x.Transactions)
            .FirstAsync(x => x.Id == sessionId.Value, cancellationToken);
    }

    internal sealed record DrawerCandidate(Guid Id, Guid? OpenedByUserId, Guid? BranchId, Guid? InventoryLocationId);

    /// <summary>
    /// The cashier's own drawer first: they hold the cash, wherever the sale was made. Otherwise the only open
    /// drawer of the sale's branch; with several open there and none of them the cashier's, guessing would put
    /// the cash in someone else's count. Cash of a branch never goes to a drawer of another branch; drawers
    /// opened before branches existed (no branch) still take it when they are the only open one.
    /// </summary>
    internal static Guid? PickDrawer(
        IReadOnlyList<DrawerCandidate> openSessions,
        Guid? userId,
        Guid? branchId,
        Guid? inventoryLocationId)
    {
        var own = openSessions.FirstOrDefault(x => userId is not null && x.OpenedByUserId == userId);
        if (own is not null)
            return own.Id;

        if (branchId is { } saleBranchId)
        {
            var atBranch = openSessions.Where(x => x.BranchId == saleBranchId).ToList();
            if (atBranch.Count > 0)
                return atBranch.Count == 1 ? atBranch[0].Id : null;

            return openSessions.Count == 1 && openSessions[0].BranchId is null ? openSessions[0].Id : null;
        }

        // A sale without branch (made before branches existed): by the location of the payment, then the business.
        var atLocation = inventoryLocationId is { } locationId
            ? openSessions.Where(x => x.InventoryLocationId == locationId).ToList()
            : [];
        return (atLocation.Count == 1 ? atLocation[0].Id : (Guid?)null)
            ?? (openSessions.Count == 1 ? openSessions[0].Id : (Guid?)null);
    }
}
