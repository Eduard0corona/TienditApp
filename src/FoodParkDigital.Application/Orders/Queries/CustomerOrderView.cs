using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders.Queries;

/// <summary>A product of the order as the customer ordered it (Fase 24C): what is still charged, with its options.</summary>
public class CustomerOrderItemDto
{
    /// <summary>For «Pedir otra vez»: the store puts it back in the cart while its menu still has it.</summary>
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = null!;
    /// <summary>Without the units cancelled from a tab, which were never served.</summary>
    public decimal Quantity { get; set; }
    /// <summary>What the line charges, with the order's tax policy like the prices of the menu.</summary>
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
    /// <summary>The options chosen for it, by name («Sin cebolla»).</summary>
    public List<string> Options { get; set; } = [];
    /// <summary>The same options by their id in the business's catalog, for «Pedir otra vez».</summary>
    public List<Guid> OptionIds { get; set; } = [];
}

/// <summary>
/// One order as its customer sees it (Fase 24C, ADR 0023), read once for the account's detail and for the tracking link.
/// Each of them chooses what it shows: the link never shows the customer's name, phone or address.
/// </summary>
internal sealed class CustomerOrderView
{
    public Guid Id { get; init; }
    public string OrderNumber { get; init; } = null!;
    public string BusinessName { get; init; } = null!;
    public string BusinessSlug { get; init; } = null!;
    public string? BusinessContactPhone { get; init; }
    public bool CanOrderAgain { get; init; }
    public BusinessType BusinessType { get; init; }
    public OrderType OrderType { get; init; }
    public OrderStatus Status { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public bool PricesIncludeTax { get; init; }
    public decimal Tax { get; init; }
    public decimal Total { get; init; }
    public decimal PaidAmount { get; init; }
    /// <summary>What the business forgave (Fase 26B): not to pay anymore.</summary>
    public decimal WrittenOffAmount { get; init; }
    public string? CancellationReason { get; init; }
    public string? TableNumber { get; init; }
    public bool HasDelivery { get; init; }
    public decimal? DeliveryFee { get; init; }
    public decimal? VisitFee { get; init; }
    public string? PostalCode { get; init; }
    public string? AddressLine { get; init; }
    public string? References { get; init; }
    public DateTimeOffset? DispatchedAt { get; init; }
    public string? CourierTrackingUrl { get; init; }
    public List<CustomerOrderItemDto> Items { get; init; } = [];
    public List<OrderStatusChange> History { get; init; } = [];

    public List<OrderTrackingStepDto> Steps()
        => OrderTracking.Steps(BusinessType, OrderType, Status, CreatedAt, History, HasDelivery, DispatchedAt);

    public (OrderTrackingStep Step, string Label) Current()
        => OrderTracking.Current(BusinessType, OrderType, Status, HasDelivery, DispatchedAt);

    /// <summary>
    /// The first of <paramref name="orders"/>, with its business. The caller decides which orders it may read: the
    /// account's own, or the one of the link inside its business.
    /// </summary>
    public static Task<CustomerOrderView?> FirstAsync(
        IApplicationDbContext context,
        IQueryable<Order> orders,
        CancellationToken cancellationToken)
        => (
            from order in orders.AsNoTracking()
            join tenant in context.Tenants.AsNoTracking() on order.TenantId equals tenant.Id
            select new CustomerOrderView
            {
                Id = order.Id,
                OrderNumber = order.OrderNumber,
                BusinessName = tenant.Name,
                BusinessSlug = tenant.Slug,
                BusinessContactPhone = tenant.ContactPhone,
                CanOrderAgain = context.TenantSettings.Any(s => s.TenantId == order.TenantId && s.OnlineMenuMode == OnlineMenuMode.Orders),
                BusinessType = order.BusinessType,
                OrderType = order.OrderType,
                Status = order.Status,
                CreatedAt = order.CreatedAt,
                PricesIncludeTax = order.PricesIncludeTax,
                Tax = order.Tax,
                Total = order.Total,
                PaidAmount = order.PaidAmount,
                WrittenOffAmount = order.WrittenOffAmount,
                CancellationReason = order.CancellationReason,
                TableNumber = order.TableNumber,
                HasDelivery = order.Delivery != null,
                DeliveryFee = order.Delivery != null ? order.Delivery.Fee : (decimal?)null,
                VisitFee = order.VisitCharge != null ? order.VisitCharge.Fee : (decimal?)null,
                PostalCode = order.Delivery != null ? order.Delivery.PostalCode : null,
                AddressLine = order.Delivery != null ? order.Delivery.AddressLine : null,
                References = order.Delivery != null ? order.Delivery.References : null,
                DispatchedAt = order.Delivery != null ? order.Delivery.DispatchedAt : null,
                CourierTrackingUrl = order.Delivery != null ? order.Delivery.TrackingUrl : null,
                // Dishes cancelled from a tab were never served.
                Items = order.Items
                    .Where(i => i.CancelledQuantity < i.Quantity)
                    .Select(i => new CustomerOrderItemDto
                    {
                        ProductId = i.ProductId,
                        ProductName = i.ProductName,
                        Quantity = i.Quantity - i.CancelledQuantity,
                        Amount = i.TotalPrice - i.CancelledAmount,
                        Notes = i.Notes,
                        Options = i.Options.Select(o => o.OptionName).ToList(),
                        OptionIds = i.Options.Select(o => o.OptionId).ToList()
                    })
                    .ToList(),
                History = order.StatusHistory
                    .Select(h => new OrderStatusChange(h.PreviousStatus, h.NewStatus, h.ChangedAt))
                    .ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);
}
