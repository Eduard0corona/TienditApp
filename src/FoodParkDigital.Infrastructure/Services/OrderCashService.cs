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
        var session = await FindDrawerAsync(order.TenantId, payment.InventoryLocationId, cancellationToken);
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
        var session = await FindDrawerAsync(order.TenantId, refund.InventoryLocationId, cancellationToken);
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
        Guid tenantId,
        Guid? inventoryLocationId,
        CancellationToken cancellationToken)
    {
        var openSessions = await _context.CashSessions
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Status == CashSessionStatus.Open)
            .Select(x => new { x.Id, x.OpenedByUserId, x.InventoryLocationId })
            .ToListAsync(cancellationToken);
        if (openSessions.Count == 0)
            return null;

        var userId = Guid.TryParse(_currentUserService.UserId, out var parsed) ? parsed : (Guid?)null;
        var atBranch = inventoryLocationId is { } locationId
            ? openSessions.Where(x => x.InventoryLocationId == locationId).ToList()
            : [];

        // The cashier's own drawer first; then a branch or a business with a single open drawer (as before
        // drawers were per cashier). With several drawers open and none of them the cashier's, guessing would
        // put the cash in someone else's count.
        var sessionId = openSessions.FirstOrDefault(x => userId is not null && x.OpenedByUserId == userId)?.Id
            ?? (atBranch.Count == 1 ? atBranch[0].Id : (Guid?)null)
            ?? (openSessions.Count == 1 ? openSessions[0].Id : (Guid?)null);
        if (sessionId is null)
            return null;

        return await _context.CashSessions
            .Include(x => x.Transactions)
            .FirstAsync(x => x.Id == sessionId.Value, cancellationToken);
    }
}
