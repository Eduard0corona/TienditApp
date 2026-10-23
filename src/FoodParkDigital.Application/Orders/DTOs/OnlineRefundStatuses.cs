using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.OnlinePayments.Enums;
using FoodParkDigital.Domain.Orders.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders.DTOs;

/// <summary>
/// Whether Mercado Pago gave back each «En línea» refund of an order (Fase 24I), why it went back, and the chargeback or claim
/// of its payment (Fase 24K).
/// </summary>
internal static class OnlineRefundStatuses
{
    public static async Task ApplyAsync(IApplicationDbContext context, OrderDto dto, CancellationToken cancellationToken)
    {
        if (dto.Channel != OrderChannel.Online)
            return;

        var dispute = await context.OnlineCheckouts
            .AsNoTracking()
            .Where(c => c.OrderId == dto.Id && c.Dispute != OnlinePaymentDispute.None)
            .Select(c => new { c.Dispute, c.DisputeUpdatedAt })
            .FirstOrDefaultAsync(cancellationToken);
        dto.OnlineDispute = dispute?.Dispute;
        dto.OnlineDisputeUpdatedAt = dispute?.DisputeUpdatedAt;

        var online = dto.Refunds.Where(r => r.Method == OrderPaymentMethod.Online).ToList();
        if (online.Count == 0)
            return;

        var statuses = await context.OnlinePaymentRefunds
            .AsNoTracking()
            .Where(r => r.OrderId == dto.Id && r.OrderRefundId != null)
            .Select(r => new { r.OrderRefundId, r.Status, r.FailureMessage, r.Reason })
            .ToListAsync(cancellationToken);
        foreach (var refund in online)
        {
            var status = statuses.FirstOrDefault(s => s.OrderRefundId == refund.Id);
            refund.OnlineStatus = status?.Status;
            refund.OnlineFailure = status?.FailureMessage;
            refund.OnlineReason = status?.Reason;
        }
    }
}
