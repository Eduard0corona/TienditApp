using FoodParkDigital.Application.Appointments.HomeVisits;
using FoodParkDigital.Application.Quotes;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Invoicing;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Appointments.OnlineBooking;
using FoodParkDigital.Domain.Quotes;
using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Domain.Messaging.Enums;
using FoodParkDigital.Domain.Orders;
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
    /// <summary>
    /// Appointments customers requested online, still ahead, that the business has not accepted nor declined (Fase 24D);
    /// urgent, those for today and those to answer today to be in time, or already late (Fase 29L).
    /// </summary>
    public const string AppointmentRequests = "appointmentRequests";
    /// <summary>CFDI still «Timbrando» past the PAC's window, checked in the SAT's portal from Reportes › CFDI emitidos (Fase 28K).</summary>
    public const string CfdisToReview = "cfdisToReview";
    /// <summary>
    /// The store publishes the main branch's address while the business goes to its customers' homes (Fase 29A,
    /// <see cref="HomeVisitAddressNotice"/>): 1 until the owner marks the branch «Solo a domicilio» or dismisses it.
    /// </summary>
    public const string HomeVisitAddress = "homeVisitAddress";
    /// <summary>
    /// Quotes the customer accepted in the last 30 days that have not become their service order yet (Fase 29G, ADR 0061);
    /// those the customer accepted from their link are urgent: the business may not know yet.
    /// </summary>
    public const string QuotesToConvert = "quotesToConvert";
}

public class GetAttentionQueryHandler : IRequestHandler<GetAttentionQuery, AttentionDto>
{
    private static readonly OrderStatus[] FinishedStatuses = [OrderStatus.Delivered, OrderStatus.Completed, OrderStatus.Cancelled];
    private static readonly int AnswerByHours = ResponseTimes.LimitHours[^1];

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
            // Placed by customers from the online menu: they wait until someone confirms or cancels them. Urgent are those of
            // the online menu (not of a table nor taken by the team) that have waited 15 minutes, halfway to counting among the
            // slowest in Explorar (Fase 29X, ADR 0078).
            var toConfirm = _context.Orders.AsNoTracking()
                .Where(o => o.Channel == OrderChannel.Online && o.Status == OrderStatus.Pending);
            var waitingSince = now.AddMinutes(-OrderConfirmTimes.WarnAfterMinutes);
            Add(AttentionKeys.OnlineOrdersToConfirm, await toConfirm.CountAsync(cancellationToken),
                await toConfirm.CountAsync(
                    o => !o.CreatedByBusiness && o.TableNumber == null && o.OpenTableId == null && o.CreatedAt <= waitingSince,
                    cancellationToken));
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
                    && m.Purpose != MessagePurpose.QuoteDocument
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
            // Every request still ahead waits for an answer. Urgent are those for today, as before, and those to answer
            // today to be in time (Fase 29L, ResponseTimes.AnswerBy: the time asked for or a day after arriving, whichever
            // comes first), or already late; a request the business moved to today is urgent though its limit is tomorrow. One
            // from the business's own team counts in no time to answer (Fase 29M): urgent only for today.
            var requests = _context.Appointments.AsNoTracking()
                .Where(a => a.Status == AppointmentStatus.Requested && a.StartAt > now);
            Add(AttentionKeys.AppointmentRequests, await requests.CountAsync(cancellationToken),
                await requests.CountAsync(
                    a => a.StartAt <= endOfToday
                        || (!a.FromTeam
                            && ((a.RequestedStartAt ?? a.StartAt) <= endOfToday
                                || a.CreatedAt.AddHours(AnswerByHours) <= endOfToday)),
                    cancellationToken));
        }

        if (Can(TenantPermission.ManageOrders) && Has(TenantCapability.Service))
        {
            var toConvert = QuotesToConvert.Of(_context.Quotes.AsNoTracking(), now);
            Add(AttentionKeys.QuotesToConvert, await toConvert.CountAsync(cancellationToken),
                await toConvert.CountAsync(q => q.ReplyChannel == QuoteReplyChannel.Link, cancellationToken));
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

            // Only who can mark the branch «Solo a domicilio» or dismiss the notice sees it.
            var tenant = await _context.Tenants.AsNoTracking().Include(t => t.Settings)
                .FirstOrDefaultAsync(t => t.Id == _access.TenantId, cancellationToken);
            if (tenant is not null && await HomeVisitAddressNotice.ShowsAsync(_context, tenant, tenant.Settings, cancellationToken))
                Add(AttentionKeys.HomeVisitAddress, 1);
        }

        return new AttentionDto { Items = items };
    }
}
