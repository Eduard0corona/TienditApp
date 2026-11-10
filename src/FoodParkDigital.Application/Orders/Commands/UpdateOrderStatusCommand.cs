using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders.Commands;

/// <summary>
/// Moves an order to another status by hand, cancelling it included. One transaction with what it takes from or returns to
/// the inventory, its folio included (Fase 31G); a save that finds the order changed meanwhile runs it again.
/// </summary>
[RequiresPermission(TenantPermission.ManageOrders)]
public record UpdateOrderStatusCommand : IRequest<Result>, IRetryOnConcurrencyConflict
{
    public Guid OrderId { get; init; }
    public OrderStatus NewStatus { get; init; }

    string? IRetryOnConcurrencyConflict.ConcurrencyConflictMessage => UpdateOrderStatusCommandHandler.UpdatedMeanwhileMessage;
}

/// <summary>The kitchen board moves an order through preparation: started and ready, nothing else.</summary>
[RequiresPermission(TenantPermission.ManageServiceBoard)]
[RequiresCapability(TenantCapability.Kitchen)]
public record MoveKitchenOrderCommand(Guid OrderId, OrderStatus NewStatus) : IRequest<Result>, IRetryOnConcurrencyConflict
{
    string? IRetryOnConcurrencyConflict.ConcurrencyConflictMessage => UpdateOrderStatusCommandHandler.UpdatedMeanwhileMessage;
}

public class UpdateOrderStatusCommandValidator : AbstractValidator<UpdateOrderStatusCommand>
{
    public UpdateOrderStatusCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.NewStatus).IsInEnum();
    }
}

public class MoveKitchenOrderCommandValidator : AbstractValidator<MoveKitchenOrderCommand>
{
    public MoveKitchenOrderCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.NewStatus)
            .Must(status => status is OrderStatus.InPreparation or OrderStatus.Ready)
            .WithMessage("La cocina solo marca un pedido en preparación o listo.");
    }
}

public class UpdateOrderStatusCommandHandler
    : IRequestHandler<UpdateOrderStatusCommand, Result>, IRequestHandler<MoveKitchenOrderCommand, Result>
{
    /// <summary>What it answers when the order kept changing after three attempts (409).</summary>
    public const string UpdatedMeanwhileMessage = "La orden fue actualizada por otro proceso. Refresca la lista e intenta nuevamente.";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IOrderInventoryService _orderInventoryService;

    public UpdateOrderStatusCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        IOrderInventoryService orderInventoryService)
    {
        _context = context;
        _currentUserService = currentUserService;
        _orderInventoryService = orderInventoryService;
    }

    public Task<Result> Handle(UpdateOrderStatusCommand request, CancellationToken cancellationToken)
        => ChangeStatusAsync(request.OrderId, request.NewStatus, cancellationToken);

    public Task<Result> Handle(MoveKitchenOrderCommand request, CancellationToken cancellationToken)
        => ChangeStatusAsync(request.OrderId, request.NewStatus, cancellationToken);

    private async Task<Result> ChangeStatusAsync(Guid orderId, OrderStatus newStatus, CancellationToken cancellationToken)
    {
        var order = await _context.Orders
            .Include(o => o.Items)
            .Include(o => o.StatusHistory)
            .Include(o => o.ServicePieces)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new EntityNotFoundException("Order", orderId);

        if (order.Status == newStatus)
            return Result.Success();

        // An open tab is closed when it is paid (CloseTabCommand); by hand it could only be cancelled.
        if (order.IsOpenTab && newStatus != OrderStatus.Cancelled)
            return Result.Failure("Es una cuenta abierta: ciérrala desde Mesas cuando esté pagada.");

        var stateMachine = OrderStateMachineFactory.Create(order.BusinessType, order.OrderType);

        if (!stateMachine.GetAllowedTransitions(order.Status).Contains(newStatus))
            return Result.Failure(
                $"No se puede cambiar de '{order.Status}' a '{newStatus}'.");

        if (newStatus == OrderStatus.Cancelled)
        {
            // Same rules as CancelOrderCommand: cancellation event, payment guard and inventory reversal.
            order.Cancel(stateMachine, _currentUserService.UserName);
            await _orderInventoryService.ReverseConsumptionForOrderAsync(order, null, cancellationToken);
        }
        else
        {
            // Read before the inventory takes its folio, whose counter waits for this transaction to end.
            var closesPaidCounterSale = newStatus == OrderStatus.Ready;
            var salesSettings = closesPaidCounterSale
                ? await TenantSalesSettings.ReadAsync(_context, order.TenantId, cancellationToken)
                : null;
            var capabilities = closesPaidCounterSale
                ? await TenantCapabilityRules.ResolveCapabilitiesAsync(_context, order.TenantId, cancellationToken)
                : default;

            order.TransitionTo(newStatus, stateMachine, _currentUserService.UserName);
            await _orderInventoryService.ApplyConsumptionForOrderAsync(order, cancellationToken);

            // A counter sale paid while the kitchen prepared it is handed over once it is ready.
            if (closesPaidCounterSale)
                CounterSaleClosing.TryClose(order, salesSettings!, capabilities, _currentUserService.UserName);
        }

        // A conflict here is retried by TransactionBehavior, from a transaction undone with its folio.
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
