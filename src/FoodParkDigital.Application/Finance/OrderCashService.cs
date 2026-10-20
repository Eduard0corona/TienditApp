using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Finance;

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

        RecordPayment(session, order, payment);
        return true;
    }

    /// <summary>
    /// A payment of an order as movements of a drawer already chosen (a courier's settlement uses the drawer of whoever
    /// receives the cash): the sale and, apart, its tip.
    /// </summary>
    public static void RecordPayment(Domain.Finance.Entities.CashSession session, Order order, OrderPayment payment)
    {
        session.RegisterTransaction(
            transactionType: CashTransactionType.SalePayment,
            amount: payment.Amount,
            occurredAt: payment.PaidAt,
            paymentMethod: payment.Method,
            inventoryLocationId: payment.InventoryLocationId,
            orderId: order.Id,
            reference: payment.Reference ?? order.OrderNumber,
            notes: payment.Notes);
        // The tip enters the same drawer as its own movement: the cut shows it apart from the sales.
        if (payment.Tip > 0)
        {
            session.RegisterTransaction(
                transactionType: CashTransactionType.Tip,
                amount: payment.Tip,
                occurredAt: payment.PaidAt,
                paymentMethod: payment.Method,
                inventoryLocationId: payment.InventoryLocationId,
                orderId: order.Id,
                reference: payment.Reference ?? order.OrderNumber,
                notes: "Propina");
        }
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

    public async Task<Guid?> RegisterCustomerBalanceAsync(
        Guid tenantId,
        CashTransactionType transactionType,
        decimal amount,
        OrderPaymentMethod method,
        DateTimeOffset occurredAt,
        string? reference,
        string? notes,
        CancellationToken cancellationToken)
    {
        if (transactionType is not (CashTransactionType.CustomerDeposit or CashTransactionType.CustomerDepositReturn))
            throw new ArgumentOutOfRangeException(nameof(transactionType), transactionType, "Only a customer's balance in their favor.");

        // Not a sale: no branch or location to follow, so the cashier's own drawer, else the only one open.
        var session = await FindDrawerAsync(tenantId, branchId: null, inventoryLocationId: null, cancellationToken);
        if (session is null)
            return null;

        session.RegisterTransaction(
            transactionType: transactionType,
            amount: amount,
            occurredAt: occurredAt,
            paymentMethod: method,
            inventoryLocationId: session.InventoryLocationId,
            reference: reference,
            notes: notes);
        return session.Id;
    }

    private Task<Domain.Finance.Entities.CashSession?> FindDrawerAsync(
        Order order,
        Guid? inventoryLocationId,
        CancellationToken cancellationToken)
        => FindDrawerAsync(order.TenantId, order.BranchId, inventoryLocationId, cancellationToken);

    private async Task<Domain.Finance.Entities.CashSession?> FindDrawerAsync(
        Guid tenantId,
        Guid? branchId,
        Guid? inventoryLocationId,
        CancellationToken cancellationToken)
    {
        var openSessions = await _context.CashSessions
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Status == CashSessionStatus.Open)
            .Select(x => new DrawerCandidate(x.Id, x.OpenedByUserId, x.BranchId, x.InventoryLocationId))
            .ToListAsync(cancellationToken);
        if (openSessions.Count == 0)
            return null;

        var userId = Guid.TryParse(_currentUserService.UserId, out var parsed) ? parsed : (Guid?)null;
        var sessionId = PickDrawer(openSessions, userId, branchId, inventoryLocationId);
        if (sessionId is null)
            return null;

        return await _context.CashSessions
            .Include(x => x.Transactions)
            .FirstAsync(x => x.Id == sessionId.Value, cancellationToken);
    }

    /// <summary>
    /// Whether a branch has an open drawer to charge its orders in (a drawer from before branches counts for any
    /// branch). The online menu takes orders only then, since every payment enters a drawer.
    /// </summary>
    public static Task<bool> HasOpenDrawerAtBranchAsync(
        IApplicationDbContext context,
        Guid tenantId,
        Guid? branchId,
        CancellationToken cancellationToken)
        => context.CashSessions.AnyAsync(
            x => x.TenantId == tenantId
                && x.Status == CashSessionStatus.Open
                && (branchId == null || x.BranchId == branchId || x.BranchId == null),
            cancellationToken);

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
