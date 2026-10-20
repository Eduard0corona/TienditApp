using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders.Queries;

/// <summary>
/// An online order followed with the link of its receipt (Fase 24C, ADR 0023), without an account: where it is, step by
/// step, and what it carries, for <see cref="OrderTracking.Days"/> days after it was placed. Nothing of the customer (name,
/// phone, address or notes) is in the answer, and a link unknown, changed, expired or of a business that is not active answers
/// the same.
/// </summary>
[AllowAnonymousRequest("Seguimiento de un pedido en línea con el enlace imposible de adivinar de su recibo, sin datos del cliente.")]
public record GetPublicOrderTrackingQuery(string Token) : IRequest<PublicOrderTrackingDto?>;

public class GetPublicOrderTrackingQueryValidator : AbstractValidator<GetPublicOrderTrackingQuery>
{
    public GetPublicOrderTrackingQueryValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(100);
    }
}

public class PublicOrderTrackingDto
{
    public string OrderNumber { get; set; } = null!;
    public string BusinessName { get; set; } = null!;
    /// <summary>Its online menu (/m/{slug}), to order again.</summary>
    public string BusinessSlug { get; set; } = null!;
    /// <summary>The business's phone, to call it about the order.</summary>
    public string? BusinessContactPhone { get; set; }
    public OrderType OrderType { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Where it is, as the customer reads it («En preparación», «En camino»).</summary>
    public OrderTrackingStep Step { get; set; }
    public string StepLabel { get; set; } = null!;
    /// <summary>The steps of its kind, in order, with the time it reached each one.</summary>
    public List<OrderTrackingStepDto> Steps { get; set; } = [];
    /// <summary>Still open: the client asks again while it is.</summary>
    public bool InProgress { get; set; }
    /// <summary>It goes to the customer's address (the address itself is not in the answer).</summary>
    public bool ToAddress { get; set; }
    /// <summary>The courier's link to follow the trip, when the business wrote it down (Fase 22C).</summary>
    public string? CourierTrackingUrl { get; set; }
    public List<CustomerOrderItemDto> Items { get; set; } = [];
    /// <summary>What the delivery charged, inside <see cref="Total"/>; null for an order picked up or served.</summary>
    public decimal? DeliveryFee { get; set; }
    /// <summary>The charge for the visit to the customer's address (Fase 22E), inside <see cref="Total"/>; null for any other order.</summary>
    public decimal? VisitFee { get; set; }
    /// <summary>When false, the IVA (<see cref="Tax"/>) is added to the prices of the products.</summary>
    public bool PricesIncludeTax { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
    public decimal PaidAmount { get; set; }
    /// <summary>What the business forgave (Fase 26B): not to pay anymore.</summary>
    public decimal WrittenOffAmount { get; set; }
    /// <summary>What the business said when it cancelled it, as the customer heard it by WhatsApp.</summary>
    public string? CancellationReason { get; set; }
}

public class GetPublicOrderTrackingQueryHandler : IRequestHandler<GetPublicOrderTrackingQuery, PublicOrderTrackingDto?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IDateTimeProvider _clock;

    public GetPublicOrderTrackingQueryHandler(IApplicationDbContext context, ICurrentTenantService tenantService, IDateTimeProvider clock)
    {
        _context = context;
        _tenantService = tenantService;
        _clock = clock;
    }

    public async Task<PublicOrderTrackingDto?> Handle(GetPublicOrderTrackingQuery request, CancellationToken cancellationToken)
    {
        if (!Order.IsTrackingToken(request.Token))
            return null;

        // The link names one order of the whole platform before any business is in context: only its business and when
        // it was placed are read here; the order itself is read inside that business.
        var now = _clock.UtcNow;
        var found = await (
                from order in _context.Orders.IgnoreQueryFilters().AsNoTracking()
                join tenant in _context.Tenants.AsNoTracking() on order.TenantId equals tenant.Id
                where order.TrackingToken == request.Token && tenant.Status == TenantStatus.Active
                select new { order.Id, order.TenantId, order.TrackingToken, order.CreatedAt })
            .FirstOrDefaultAsync(cancellationToken);
        // The column compares without case, as the database does; the link is the exact one. Followed only for its days.
        if (found is null || !string.Equals(found.TrackingToken, request.Token, StringComparison.Ordinal)
            || found.CreatedAt < now.AddDays(-OrderTracking.Days))
            return null;

        _tenantService.SetTenant(found.TenantId);
        var view = await CustomerOrderView.FirstAsync(_context, _context.Orders.Where(o => o.Id == found.Id), cancellationToken);
        if (view is null)
            return null;

        // What the customer wrote is only for the account's detail: a link travels further than a session.
        foreach (var item in view.Items)
            item.Notes = null;

        var (step, label) = view.Current();
        return new PublicOrderTrackingDto
        {
            OrderNumber = view.OrderNumber,
            BusinessName = view.BusinessName,
            BusinessSlug = view.BusinessSlug,
            BusinessContactPhone = view.BusinessContactPhone,
            OrderType = view.OrderType,
            CreatedAt = view.CreatedAt,
            Step = step,
            StepLabel = label,
            Steps = view.Steps(),
            InProgress = OrderTracking.IsInProgress(view.Status, view.CreatedAt, now),
            ToAddress = view.HasDelivery,
            CourierTrackingUrl = view.CourierTrackingUrl,
            Items = view.Items,
            DeliveryFee = view.DeliveryFee,
            VisitFee = view.VisitFee,
            PricesIncludeTax = view.PricesIncludeTax,
            Tax = view.Tax,
            Total = view.Total,
            PaidAmount = view.PaidAmount,
            WrittenOffAmount = view.WrittenOffAmount,
            CancellationReason = view.CancellationReason
        };
    }
}
