using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders;

/// <summary>What one refund gives back and returns.</summary>
public sealed record OrderRefundInput(
    decimal Amount,
    OrderPaymentMethod Method,
    string Reason,
    bool RestockItems,
    IReadOnlyCollection<(Guid OrderItemId, decimal Quantity)> ReturnedItems,
    Guid? InventoryLocationId = null,
    string? Reference = null);

/// <summary>
/// Applies a refund to an order, the drawer and inventory, leaving everything to the caller's SaveChanges.
/// The requests that use it declare who may refund: <see cref="TenantPermission.ProcessRefunds"/>.
/// </summary>
public sealed class OrderRefundProcessor
{
    public const string NoOpenDrawerMessage = "Abre tu caja para devolver efectivo.";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IOrderCashService _orderCashService;
    private readonly IOrderInventoryService _orderInventoryService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public OrderRefundProcessor(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        IOrderCashService orderCashService,
        IOrderInventoryService orderInventoryService,
        IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _currentUserService = currentUserService;
        _orderCashService = orderCashService;
        _orderInventoryService = orderInventoryService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<OrderRefund>> RefundAsync(Order order, OrderRefundInput input, CancellationToken cancellationToken)
    {
        if (input.InventoryLocationId is { } locationId
            && !await _context.InventoryLocations.AnyAsync(x => x.Id == locationId && x.IsActive, cancellationToken))
        {
            return Result.Failure<OrderRefund>("La sucursal seleccionada no es válida.");
        }

        OrderRefund refund;
        try
        {
            refund = order.RegisterRefund(
                amount: input.Amount,
                method: input.Method,
                reason: input.Reason,
                // Stamped by the server, like payments: a client date could rewrite closed cuts.
                refundedAt: _dateTimeProvider.UtcNow,
                refundedBy: _currentUserService.UserName ?? "system",
                inventoryLocationId: input.InventoryLocationId,
                reference: input.Reference,
                restockItems: input.RestockItems && input.ReturnedItems.Count > 0,
                returnedItems: input.ReturnedItems);
        }
        catch (DomainException ex)
        {
            return Result.Failure<OrderRefund>(ex.Message);
        }

        var inDrawer = await _orderCashService.RegisterOrderRefundAsync(order, refund, cancellationToken);
        if (!inDrawer && input.Method == OrderPaymentMethod.Cash)
        {
            var settings = await TenantSalesSettings.ReadAsync(_context, order.TenantId, cancellationToken);
            if (settings.RequireOpenCashSession)
                return Result.Failure<OrderRefund>(NoOpenDrawerMessage);
        }

        if (refund.RestockItems)
            await _orderInventoryService.ReturnItemsToInventoryAsync(order, refund, cancellationToken);

        return Result.Success(refund);
    }
}
