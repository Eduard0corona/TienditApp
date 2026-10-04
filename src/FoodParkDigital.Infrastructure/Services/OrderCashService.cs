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
        var openSessions = await _context.CashSessions
            .AsNoTracking()
            .Where(x => x.TenantId == order.TenantId && x.Status == CashSessionStatus.Open)
            .Select(x => new { x.Id, x.OpenedByUserId, x.InventoryLocationId })
            .ToListAsync(cancellationToken);
        if (openSessions.Count == 0)
            return false;

        var userId = Guid.TryParse(_currentUserService.UserId, out var parsed) ? parsed : (Guid?)null;
        var atBranch = payment.InventoryLocationId is { } locationId
            ? openSessions.Where(x => x.InventoryLocationId == locationId).ToList()
            : [];

        // The cashier's own drawer first; then a branch or a business with a single open drawer (as before
        // drawers were per cashier). With several drawers open and none of them the cashier's, guessing would
        // put the cash in someone else's count.
        var sessionId = openSessions.FirstOrDefault(x => userId is not null && x.OpenedByUserId == userId)?.Id
            ?? (atBranch.Count == 1 ? atBranch[0].Id : (Guid?)null)
            ?? (openSessions.Count == 1 ? openSessions[0].Id : (Guid?)null);
        if (sessionId is null)
            return false;

        var session = await _context.CashSessions
            .Include(x => x.Transactions)
            .FirstAsync(x => x.Id == sessionId.Value, cancellationToken);

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
}
