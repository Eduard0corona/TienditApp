using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders.Queries;

/// <summary>
/// The customer account's own orders, in every business where they were placed with this account (or claimed by
/// confirming the phone). Only the account's orders: the business filter is replaced by the account filter.
/// </summary>
[RequiresAuthentication]
public record GetMyOrdersQuery : IRequest<PaginatedList<MyOrderDto>>
{
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 10;
    /// <summary>The orders in progress, the ones before, or all of them (Fase 24C).</summary>
    public MyOrdersScope Scope { get; init; } = MyOrdersScope.All;
}

public enum MyOrdersScope
{
    All = 0,
    /// <summary>Open and placed in the last <see cref="OrderTracking.Days"/> days: the client follows them.</summary>
    InProgress = 1,
    /// <summary>Delivered, finished, cancelled, or left open by the business for longer than that.</summary>
    Past = 2
}

public class GetMyOrdersQueryValidator : AbstractValidator<GetMyOrdersQuery>
{
    public GetMyOrdersQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 50);
        RuleFor(x => x.Scope).IsInEnum();
    }
}

public class MyOrderDto
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = null!;
    public string BusinessName { get; set; } = null!;
    /// <summary>For "Pedir otra vez" in the business's online menu (/m/{slug}).</summary>
    public string BusinessSlug { get; set; } = null!;
    public bool CanOrderAgain { get; set; }
    public OrderStatus Status { get; set; }
    public OrderType OrderType { get; set; }
    /// <summary>Where it is, as the customer reads it («En preparación», «En camino», Fase 24C).</summary>
    public OrderTrackingStep Step { get; set; }
    public string StepLabel { get; set; } = null!;
    public decimal Total { get; set; }
    public decimal PaidAmount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<MyOrderItemDto> Items { get; set; } = [];
}

public class MyOrderItemDto
{
    /// <summary>For «Pedir otra vez» (Fase 24B): the store puts it back in the cart while its menu still has it.</summary>
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = null!;
    public decimal Quantity { get; set; }
    /// <summary>The options chosen for it («Sin cebolla»), by their id in the business's catalog.</summary>
    public List<Guid> OptionIds { get; set; } = [];
}

public class GetMyOrdersQueryHandler : IRequestHandler<GetMyOrdersQuery, PaginatedList<MyOrderDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;

    public GetMyOrdersQueryHandler(IApplicationDbContext context, ICurrentUserService currentUser, IDateTimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<PaginatedList<MyOrderDto>> Handle(GetMyOrdersQuery request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUser.UserId, out var userId))
            return new PaginatedList<MyOrderDto>([], 0, request.PageNumber, request.PageSize);

        var orders = _context.Orders.IgnoreQueryFilters().AsNoTracking().Where(o => o.CustomerUserId == userId);
        // OrderTracking.IsInProgress, in SQL.
        var since = _clock.UtcNow.AddDays(-OrderTracking.Days);
        orders = request.Scope switch
        {
            MyOrdersScope.InProgress => orders.Where(o => o.Status != OrderStatus.Delivered && o.Status != OrderStatus.Completed
                && o.Status != OrderStatus.Cancelled && o.CreatedAt >= since),
            MyOrdersScope.Past => orders.Where(o => o.Status == OrderStatus.Delivered || o.Status == OrderStatus.Completed
                || o.Status == OrderStatus.Cancelled || o.CreatedAt < since),
            _ => orders
        };

        var query =
            from order in orders
            join tenant in _context.Tenants.AsNoTracking() on order.TenantId equals tenant.Id
            orderby order.CreatedAt descending
            select new MyOrderRow(new MyOrderDto
            {
                Id = order.Id,
                OrderNumber = order.OrderNumber,
                BusinessName = tenant.Name,
                BusinessSlug = tenant.Slug,
                CanOrderAgain = _context.TenantSettings.Any(s => s.TenantId == order.TenantId && s.OnlineMenuMode == OnlineMenuMode.Orders),
                Status = order.Status,
                OrderType = order.OrderType,
                Total = order.Total,
                PaidAmount = order.PaidAmount,
                CreatedAt = order.CreatedAt,
                // Dishes cancelled from a tab were never served.
                Items = order.Items
                    .Where(i => i.CancelledQuantity < i.Quantity)
                    .Select(i => new MyOrderItemDto
                    {
                        ProductId = i.ProductId,
                        ProductName = i.ProductName,
                        Quantity = i.Quantity - i.CancelledQuantity,
                        OptionIds = i.Options.Select(o => o.OptionId).ToList()
                    })
                    .ToList()
            }, order.BusinessType, order.Delivery != null, order.Delivery != null ? order.Delivery.DispatchedAt : null);

        var page = await PaginatedList<MyOrderRow>.CreateAsync(query, request.PageNumber, request.PageSize, cancellationToken);
        foreach (var row in page.Items)
            (row.Order.Step, row.Order.StepLabel) = OrderTracking.Current(
                row.BusinessType, row.Order.OrderType, row.Order.Status, row.HasDelivery, row.DispatchedAt);
        return new PaginatedList<MyOrderDto>(
            page.Items.Select(row => row.Order).ToList(), page.TotalCount, request.PageNumber, request.PageSize);
    }

    /// <summary>An order of the list with what its step is told by.</summary>
    private sealed record MyOrderRow(MyOrderDto Order, BusinessType BusinessType, bool HasDelivery, DateTimeOffset? DispatchedAt);
}
