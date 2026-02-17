using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Services;

public class OrderCashService : IOrderCashService
{
    private readonly IApplicationDbContext _context;

    public OrderCashService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task RegisterOrderPaymentAsync(Order order, OrderPayment payment, CancellationToken cancellationToken)
    {
        var openSession = await _context.CashSessions
            .Include(x => x.Transactions)
            .Where(x => x.TenantId == order.TenantId && x.Status == CashSessionStatus.Open)
            .OrderByDescending(x => x.OpenedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (openSession is null)
            return;

        openSession.RegisterTransaction(
            transactionType: CashTransactionType.SalePayment,
            amount: payment.Amount,
            occurredAt: payment.PaidAt,
            paymentMethod: payment.Method,
            orderId: order.Id,
            reference: payment.Reference ?? order.OrderNumber,
            notes: payment.Notes);
    }
}
