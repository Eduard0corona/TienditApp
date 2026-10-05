using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders.Commands;

/// <summary>Gives money back for an order, optionally for returned units that go back to inventory.</summary>
[RequiresPermission(TenantPermission.ManageOrders)]
public record RefundOrderCommand : IRequest<Result<OrderDto>>
{
    public Guid OrderId { get; init; }
    public decimal Amount { get; init; }
    public OrderPaymentMethod Method { get; init; }
    public string Reason { get; init; } = string.Empty;
    public bool RestockItems { get; init; } = true;
    public List<RefundItemDto> Items { get; init; } = [];
    public Guid? InventoryLocationId { get; init; }
    public string? Reference { get; init; }
}

public record RefundItemDto
{
    public Guid OrderItemId { get; init; }
    /// <summary>Up to three decimals for items sold by weight, volume, length or time.</summary>
    public decimal Quantity { get; init; }
}

public class RefundOrderCommandValidator : AbstractValidator<RefundOrderCommand>
{
    public RefundOrderCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0).PrecisionScale(18, 2, ignoreTrailingZeros: true);
        RuleFor(x => x.Method).IsInEnum();
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Escribe el motivo del reembolso.")
            .MaximumLength(OrderRefund.MaxReasonLength);
        RuleFor(x => x.Reference).MaximumLength(120);
        RuleFor(x => x.Items).Must(items => items.Count <= OrderLimits.MaxItemsPerOrder)
            .WithMessage($"Una devolución admite como máximo {OrderLimits.MaxItemsPerOrder} líneas.");
        RuleFor(x => x.Items).Must(items => items.Select(i => i.OrderItemId).Distinct().Count() == items.Count)
            .WithMessage("Cada artículo se indica una sola vez.");
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.OrderItemId).NotEmpty();
            item.RuleFor(i => i.Quantity).GreaterThan(0).LessThanOrEqualTo(OrderLimits.MaxQuantityPerLine)
                .PrecisionScale(18, 3, ignoreTrailingZeros: true);
        });
    }
}

public class RefundOrderCommandHandler : IRequestHandler<RefundOrderCommand, Result<OrderDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly OrderRefundProcessor _refunds;

    public RefundOrderCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        ITenantAccessService tenantAccessService,
        IOrderCashService orderCashService,
        IDateTimeProvider dateTimeProvider,
        IOrderInventoryService orderInventoryService)
    {
        _context = context;
        _refunds = new OrderRefundProcessor(
            context,
            currentUserService,
            tenantAccessService,
            orderCashService,
            orderInventoryService,
            dateTimeProvider);
    }

    public async Task<Result<OrderDto>> Handle(RefundOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await OrderRefundQueries.LoadForRefundAsync(_context, request.OrderId, cancellationToken)
            ?? throw new EntityNotFoundException("Order", request.OrderId);

        if (!await _refunds.CanRefundAsync(order.TenantId, cancellationToken))
            return Result.Failure<OrderDto>(OrderRefundProcessor.NotAllowedMessage);

        var refund = await _refunds.RefundAsync(
            order,
            new OrderRefundInput(
                request.Amount,
                request.Method,
                request.Reason,
                request.RestockItems,
                request.Items.Select(i => (i.OrderItemId, i.Quantity)).ToList(),
                request.InventoryLocationId,
                request.Reference),
            cancellationToken);
        if (refund.IsFailure)
            return Result.Failure<OrderDto>(refund.Error!);

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(await OrderRefundQueries.ToDtoAsync(_context, order.Id, cancellationToken));
    }
}

internal static class OrderRefundQueries
{
    public static Task<Order?> LoadForRefundAsync(IApplicationDbContext context, Guid orderId, CancellationToken cancellationToken)
        => context.Orders
            .Include(o => o.Items)
            .Include(o => o.Payments)
            .Include(o => o.Refunds).ThenInclude(r => r.Lines)
            .Include(o => o.StatusHistory)
            .Include(o => o.ServicePieces)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

    public static async Task<OrderDto> ToDtoAsync(IApplicationDbContext context, Guid orderId, CancellationToken cancellationToken)
    {
        var order = await context.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.StatusHistory)
            .Include(o => o.ServiceDetail)
            .Include(o => o.ServicePieces)
            .Include(o => o.Payments)
            .Include(o => o.Refunds).ThenInclude(r => r.Lines)
            .FirstAsync(o => o.Id == orderId, cancellationToken);
        var dto = order.ToDto();
        dto.ApplyStateMetadata();
        return dto;
    }
}
