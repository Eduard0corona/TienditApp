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

/// <summary>Moves an order to another status by hand, cancelling it included.</summary>
[RequiresPermission(TenantPermission.ManageOrders)]
public record UpdateOrderStatusCommand : IRequest<Result>
{
    public Guid OrderId { get; init; }
    public OrderStatus NewStatus { get; init; }
}

/// <summary>The kitchen board moves an order through preparation: started and ready, nothing else.</summary>
[RequiresPermission(TenantPermission.ManageServiceBoard)]
[RequiresCapability(TenantCapability.Kitchen)]
public record MoveKitchenOrderCommand(Guid OrderId, OrderStatus NewStatus) : IRequest<Result>;

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
        const int maxAttempts = 3;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
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
                order.TransitionTo(newStatus, stateMachine, _currentUserService.UserName);
                await _orderInventoryService.ApplyConsumptionForOrderAsync(order, cancellationToken);

                // A counter sale paid while the kitchen prepared it is handed over once it is ready.
                if (newStatus == OrderStatus.Ready)
                {
                    var salesSettings = await TenantSalesSettings.ReadAsync(_context, order.TenantId, cancellationToken);
                    var capabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(_context, order.TenantId, cancellationToken);
                    CounterSaleClosing.TryClose(order, salesSettings, capabilities, _currentUserService.UserName);
                }
            }

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                return Result.Success();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (_context is DbContext dbContext)
                    dbContext.ChangeTracker.Clear();
            }
        }

        return Result.Failure(
            "La orden fue actualizada por otro proceso. Refresca la lista e intenta nuevamente.");
    }
}
