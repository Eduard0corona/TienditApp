using AutoMapper;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;

namespace FoodParkDigital.Application.Orders.Commands;

/// <summary>
/// Voids a paid sale in one step: refunds what is left of each payment with its own method, returns every
/// unit not returned yet and cancels the order when its state allows it. A delivered or completed order
/// cannot be cancelled: it stays in its state, fully refunded.
/// </summary>
[RequiresPermission(TenantPermission.ManageOrders)]
public record VoidOrderCommand : IRequest<Result<OrderDto>>
{
    public Guid OrderId { get; init; }
    public string Reason { get; init; } = string.Empty;
    public bool RestockItems { get; init; } = true;
}

public class VoidOrderCommandValidator : AbstractValidator<VoidOrderCommand>
{
    public VoidOrderCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Escribe el motivo de la anulación.")
            .MaximumLength(OrderRefund.MaxReasonLength);
    }
}

public class VoidOrderCommandHandler : IRequestHandler<VoidOrderCommand, Result<OrderDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly OrderRefundProcessor _refunds;
    private readonly IOrderInventoryService _orderInventoryService;
    private readonly IMapper _mapper;

    public VoidOrderCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        ITenantAccessService tenantAccessService,
        IOrderCashService orderCashService,
        IDateTimeProvider dateTimeProvider,
        IMapper mapper,
        IOrderInventoryService? orderInventoryService = null)
    {
        _context = context;
        _currentUserService = currentUserService;
        _mapper = mapper;
        _orderInventoryService = orderInventoryService ?? NoOpOrderInventoryService.Instance;
        _refunds = new OrderRefundProcessor(
            context,
            currentUserService,
            tenantAccessService,
            orderCashService,
            _orderInventoryService,
            dateTimeProvider);
    }

    public async Task<Result<OrderDto>> Handle(VoidOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await OrderRefundQueries.LoadForRefundAsync(_context, request.OrderId, cancellationToken)
            ?? throw new EntityNotFoundException("Order", request.OrderId);

        if (!await _refunds.CanRefundAsync(order.TenantId, cancellationToken))
            return Result.Failure<OrderDto>(OrderRefundProcessor.NotAllowedMessage);
        if (order.Status == Domain.Orders.Enums.OrderStatus.Cancelled)
            return Result.Failure<OrderDto>("La orden ya está cancelada.");

        // Each payment method gets back what is left of it, so cash leaves the drawer only for cash paid.
        var remainingByMethod = order.Payments
            .GroupBy(p => p.Method)
            .Select(g => (Method: g.Key, Amount: g.Sum(p => p.Amount) - order.Refunds.Where(r => r.Method == g.Key).Sum(r => r.Amount)))
            .Where(x => x.Amount > 0)
            .ToList();
        // Refunds made with a method the order was not paid with are taken from the largest remainders.
        var unassignedRefunds = order.Refunds
            .Where(r => order.Payments.All(p => p.Method != r.Method))
            .Sum(r => r.Amount);
        foreach (var index in remainingByMethod.Select((x, i) => i).OrderByDescending(i => remainingByMethod[i].Amount))
        {
            if (unassignedRefunds <= 0)
                break;
            var taken = Math.Min(unassignedRefunds, remainingByMethod[index].Amount);
            remainingByMethod[index] = (remainingByMethod[index].Method, remainingByMethod[index].Amount - taken);
            unassignedRefunds -= taken;
        }

        var unitsToReturn = order.Items
            .Where(i => i.ReturnableQuantity > 0)
            .Select(i => (i.Id, i.ReturnableQuantity))
            .ToList();
        var first = true;
        foreach (var (method, amount) in remainingByMethod.Where(x => x.Amount > 0))
        {
            // The units go back once, with the first refund.
            var refund = await _refunds.RefundAsync(
                order,
                new OrderRefundInput(amount, method, request.Reason, request.RestockItems, first ? unitsToReturn : []),
                cancellationToken);
            if (refund.IsFailure)
                return Result.Failure<OrderDto>(refund.Error!);
            first = false;
        }

        // With nothing paid there is no refund to carry the returned units: cancelling reverses the inventory
        // of the whole order instead.
        var stateMachine = OrderStateMachineFactory.Create(order.BusinessType, order.OrderType);
        if (first && !stateMachine.CanCancel(order.Status))
            return Result.Failure<OrderDto>("La orden no tiene pagos que reembolsar y su estado ya no permite cancelarla.");

        if (stateMachine.CanCancel(order.Status))
        {
            try
            {
                order.Cancel(stateMachine, _currentUserService.UserName, request.Reason.Trim());
            }
            catch (DomainException ex)
            {
                return Result.Failure<OrderDto>(ex.Message);
            }

            // Units returned with a refund were already re-entered; an unpaid order reverses what is left of its
            // consumption (the reversal discounts earlier returns).
            if (first && request.RestockItems)
                await _orderInventoryService.ReverseConsumptionForOrderAsync(order, request.Reason.Trim(), cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(await OrderRefundQueries.ToDtoAsync(_context, _mapper, order.Id, cancellationToken));
    }
}
