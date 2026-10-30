using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Invoicing;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Domain.Messaging.Enums;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Attention;

/// <summary>What needs someone now, counted only for what the person may handle: the bell and "Requiere atención".</summary>
[RequiresTenantMember]
public record GetAttentionQuery : IRequest<AttentionDto>;

public class AttentionDto
{
    public List<AttentionItemDto> Items { get; set; } = [];
}

public class AttentionItemDto
{
    /// <summary>Stable key the screens name and link: see <see cref="AttentionKeys"/>.</summary>
    public string Key { get; set; } = null!;
    public int Count { get; set; }
    /// <summary>The part that cannot wait (out of stock, critical supplies); 0 when everything counts the same.</summary>
    public int UrgentCount { get; set; }
}

public static class AttentionKeys
{
    public const string OnlineOrdersToConfirm = "onlineOrdersToConfirm";
    public const string LowStock = "lowStock";
    public const string DrawersOpenSinceYesterday = "drawersOpenSinceYesterday";
    public const string FailedMessages = "failedMessages";
    public const string UnreadChats = "unreadChats";
    public const string LateServices = "lateServices";
    public const string AppointmentsToConfirm = "appointmentsToConfirm";
    /// <summary>Appointments customers requested online that the business has not accepted nor declined (Fase 24D).</summary>
    public const string AppointmentRequests = "appointmentRequests";
    /// <summary>CFDI still «Timbrando» past the PAC's window, checked in the SAT's portal from Reportes › CFDI emitidos (Fase 28K).</summary>
    public const string CfdisToReview = "cfdisToReview";
}

public class GetAttentionQueryHandler : IRequestHandler<GetAttentionQuery, AttentionDto>
{
    private static readonly OrderStatus[] FinishedStatuses = [OrderStatus.Delivered, OrderStatus.Completed, OrderStatus.Cancelled];

    private readonly IApplicationDbContext _context;
    private readonly CurrentAccessContext _access;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;

    public GetAttentionQueryHandler(
        IApplicationDbContext context,
        CurrentAccessContext access,
        ICurrentUserService currentUser,
        IDateTimeProvider clock)
    {
        _context = context;
        _access = access;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<AttentionDto> Handle(GetAttentionQuery request, CancellationToken cancellationToken)
    {
        var permissions = await _access.GetPermissionsAsync(cancellationToken);
        var capabilities = await _access.GetCapabilitiesAsync(cancellationToken);
        var isManager = await _access.IsTenantManagerAsync(cancellationToken);
        bool Can(TenantPermission permission) => permissions.Contains(permission.ToString(), StringComparer.OrdinalIgnoreCase);
        bool Has(TenantCapability capability) => (capabilities & capability) != 0;
        var commerce = Has(TenantCapability.Retail | TenantCapability.Service | TenantCapability.Food);

        var now = _clock.UtcNow;
        var timeZoneId = await _context.TenantSettings.AsNoTracking()
            .Where(s => s.TenantId == _access.TenantId)
            .Select(s => s.TimeZone)
            .FirstOrDefaultAsync(cancellationToken);
        var zone = TenantDateTimeResolver.Resolve(timeZoneId);
        var localToday = TimeZoneInfo.ConvertTime(now, zone).Date;
        var startOfToday = TenantDateTimeResolver.LocalDateBoundaryToUtc(localToday, true, zone);
        var endOfToday = TenantDateTimeResolver.LocalDateBoundaryToUtc(localToday, false, zone);

        var items = new List<AttentionItemDto>();
        void Add(string key, int count, int urgent = 0)
        {
            if (count > 0)
                items.Add(new AttentionItemDto { Key = key, Count = count, UrgentCount = urgent });
        }

        if (commerce && Can(TenantPermission.ManageOrders))
        {
            // Placed by customers from the online menu: they wait until someone confirms or cancels them.
            Add(AttentionKeys.OnlineOrdersToConfirm, await _context.Orders.AsNoTracking()
                .CountAsync(o => o.Channel == OrderChannel.Online && o.Status == OrderStatus.Pending, cancellationToken));
        }

        if (Can(TenantPermission.ManageInventory) && Has(TenantCapability.Inventory))
        {
            var low = from stock in _context.InventoryStocks.AsNoTracking()
                      join supply in _context.SupplyItems.AsNoTracking() on stock.SupplyItemId equals supply.Id
                      where supply.IsActive && stock.OnHandQuantity <= supply.ReorderPoint
                      select new { stock.OnHandQuantity, supply.IsCritical };
            var lowCount = await low.CountAsync(cancellationToken);
            var urgent = lowCount == 0 ? 0 : await low.CountAsync(x => x.OnHandQuantity <= 0 || x.IsCritical, cancellationToken);
            Add(AttentionKeys.LowStock, lowCount, urgent);
        }

        if (commerce && Can(TenantPermission.ManageCash))
        {
            // A drawer left open since another day mixes two days of cash: a cashier sees theirs, managers every one.
            var drawers = _context.CashSessions.AsNoTracking()
                .Where(s => s.Status == CashSessionStatus.Open && s.OpenedAt < startOfToday);
            if (!isManager)
            {
                var userId = Guid.TryParse(_currentUser.UserId, out var id) ? id : Guid.Empty;
                drawers = drawers.Where(s => s.OpenedByUserId == userId);
            }

            Add(AttentionKeys.DrawersOpenSinceYesterday, await drawers.CountAsync(cancellationToken));
        }

        if (commerce && (Can(TenantPermission.ManageCampaigns) || Can(TenantPermission.ManageWhatsApp)))
        {
            var since = now.AddHours(-24);
            var failed = await _context.OutgoingMessages.AsNoTracking()
                .CountAsync(m => m.Status == OutgoingMessageStatus.Failed && m.Purpose != MessagePurpose.CfdiDocument
                    && (m.UpdatedAt ?? m.CreatedAt) >= since, cancellationToken);
            Add(AttentionKeys.FailedMessages, failed, failed);
        }

        if (commerce && Can(TenantPermission.ManageWhatsApp))
        {
            Add(AttentionKeys.UnreadChats, await _context.WhatsAppConversations.AsNoTracking()
                .CountAsync(c => c.UnreadCount > 0, cancellationToken));
        }

        if (Can(TenantPermission.ManageServiceBoard) && Has(TenantCapability.Service))
        {
            var late = from detail in _context.ServiceOrderDetails.AsNoTracking()
                       join order in _context.Orders.AsNoTracking() on detail.OrderId equals order.Id
                       where detail.PromisedAt != null && detail.PromisedAt < now && !FinishedStatuses.Contains(order.Status)
                       select detail.Id;
            Add(AttentionKeys.LateServices, await late.CountAsync(cancellationToken));
        }

        if (Can(TenantPermission.ManageAppointments) && Has(TenantCapability.Appointments))
        {
            Add(AttentionKeys.AppointmentsToConfirm, await _context.Appointments.AsNoTracking()
                .CountAsync(a => a.Status == AppointmentStatus.Scheduled && a.StartAt >= now && a.StartAt <= endOfToday, cancellationToken));
            // Every request still ahead waits for an answer; those of today are urgent.
            var requests = _context.Appointments.AsNoTracking()
                .Where(a => a.Status == AppointmentStatus.Requested && a.StartAt > now);
            Add(AttentionKeys.AppointmentRequests, await requests.CountAsync(cancellationToken),
                await requests.CountAsync(a => a.StartAt <= endOfToday, cancellationToken));
        }

        if (isManager)
        {
            // Only the SAT's portal tells now whether they were stamped; until someone checks, what they cover stays unsettled.
            var oldestInWindow = now - StampPendingInvoicesCommandHandler.RetryWindow;
            var toReview = await _context.OrderInvoices.AsNoTracking()
                    .CountAsync(i => i.Status == OrderInvoiceStatus.Stamping && i.RequestedAt < oldestInWindow, cancellationToken)
                + await _context.GlobalInvoices.AsNoTracking()
                    .CountAsync(i => i.Status == OrderInvoiceStatus.Stamping && i.RequestedAt < oldestInWindow, cancellationToken)
                + await _context.PaymentComplements.AsNoTracking()
                    .CountAsync(c => c.Status == OrderInvoiceStatus.Stamping && c.RequestedAt < oldestInWindow, cancellationToken)
                + await _context.CreditNotes.AsNoTracking()
                    .CountAsync(n => n.Status == OrderInvoiceStatus.Stamping && n.RequestedAt < oldestInWindow, cancellationToken);
            Add(AttentionKeys.CfdisToReview, toReview, toReview);
        }

        return new AttentionDto { Items = items };
    }
}
