using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Orders.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders.Queries;

/// <summary>
/// One order of the customer account (Fase 24C, ADR 0023): where it is, step by step with the time of each one, what it
/// carries and where it goes. Only an order of the account itself; any other is not found, whatever business it is in.
/// </summary>
[RequiresAuthentication]
public record GetMyOrderQuery(Guid Id) : IRequest<MyOrderDetailDto?>;

public class MyOrderDetailDto
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = null!;
    public string BusinessName { get; set; } = null!;
    /// <summary>For «Pedir otra vez» in the business's online menu (/m/{slug}).</summary>
    public string BusinessSlug { get; set; } = null!;
    /// <summary>The business's phone, to call it about the order.</summary>
    public string? BusinessContactPhone { get; set; }
    public bool CanOrderAgain { get; set; }
    public OrderStatus Status { get; set; }
    public OrderType OrderType { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Where it is, as the customer reads it («En preparación», «En camino»).</summary>
    public OrderTrackingStep Step { get; set; }
    public string StepLabel { get; set; } = null!;
    /// <summary>The steps of its kind, in order, with the time it reached each one.</summary>
    public List<OrderTrackingStepDto> Steps { get; set; } = [];
    /// <summary>
    /// Still followed: open and placed in the last <see cref="OrderTracking.Days"/> days. The client asks again while it is.
    /// </summary>
    public bool InProgress { get; set; }
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
    /// <summary>What the business said when it cancelled it, as the customer heard it by WhatsApp.</summary>
    public string? CancellationReason { get; set; }
    /// <summary>The table it was ordered from («Mesa 4»).</summary>
    public string? TableNumber { get; set; }
    /// <summary>Where it goes; none for an order picked up or served.</summary>
    public MyOrderDeliveryDto? Delivery { get; set; }
}

public class MyOrderDeliveryDto
{
    public string PostalCode { get; set; } = null!;
    public string AddressLine { get; set; } = null!;
    public string? References { get; set; }
}

public class GetMyOrderQueryHandler : IRequestHandler<GetMyOrderQuery, MyOrderDetailDto?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;

    public GetMyOrderQueryHandler(IApplicationDbContext context, ICurrentUserService currentUser, IDateTimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<MyOrderDetailDto?> Handle(GetMyOrderQuery request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUser.UserId, out var userId))
            return null;

        // The account's orders are in every business: the business filter is replaced by the account filter, as in its list.
        var order = await CustomerOrderView.FirstAsync(
            _context,
            _context.Orders.IgnoreQueryFilters().Where(o => o.Id == request.Id && o.CustomerUserId == userId),
            cancellationToken);
        if (order is null)
            return null;

        var (step, label) = order.Current();
        return new MyOrderDetailDto
        {
            Id = order.Id,
            OrderNumber = order.OrderNumber,
            BusinessName = order.BusinessName,
            BusinessSlug = order.BusinessSlug,
            BusinessContactPhone = order.BusinessContactPhone,
            CanOrderAgain = order.CanOrderAgain,
            Status = order.Status,
            OrderType = order.OrderType,
            CreatedAt = order.CreatedAt,
            Step = step,
            StepLabel = label,
            Steps = order.Steps(),
            InProgress = OrderTracking.IsInProgress(order.Status, order.CreatedAt, _clock.UtcNow),
            CourierTrackingUrl = order.CourierTrackingUrl,
            Items = order.Items,
            DeliveryFee = order.DeliveryFee,
            VisitFee = order.VisitFee,
            PricesIncludeTax = order.PricesIncludeTax,
            Tax = order.Tax,
            Total = order.Total,
            PaidAmount = order.PaidAmount,
            CancellationReason = order.CancellationReason,
            TableNumber = order.TableNumber,
            Delivery = order.HasDelivery
                ? new MyOrderDeliveryDto { PostalCode = order.PostalCode!, AddressLine = order.AddressLine!, References = order.References }
                : null
        };
    }
}
