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

[RequiresPermission(TenantPermission.ManageOrders, TenantPermission.ManageServiceBoard)]
public record UpdateOrderStatusCommand : IRequest<Result>
{
    public Guid OrderId { get; init; }
    public OrderStatus NewStatus { get; init; }
}

public class UpdateOrderStatusCommandValidator : AbstractValidator<UpdateOrderStatusCommand>
{
    public UpdateOrderStatusCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.NewStatus).IsInEnum();
    }
}

public class UpdateOrderStatusCommandHandler : IRequestHandler<UpdateOrderStatusCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IOrderInventoryService _orderInventoryService;

    public UpdateOrderStatusCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        IOrderInventoryService? orderInventoryService = null)
    {
        _context = context;
        _currentUserService = currentUserService;
        _orderInventoryService = orderInventoryService ?? NoOpOrderInventoryService.Instance;
    }

    public async Task<Result> Handle(UpdateOrderStatusCommand request, CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var order = await _context.Orders
                .Include(o => o.Items)
                .Include(o => o.StatusHistory)
                .Include(o => o.ServicePieces)
                .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken)
                ?? throw new EntityNotFoundException("Order", request.OrderId);

            if (order.Status == request.NewStatus)
                return Result.Success();

            // An open tab is closed when it is paid (CloseTabCommand); by hand it could only be cancelled.
            if (order.IsOpenTab && request.NewStatus != OrderStatus.Cancelled)
                return Result.Failure("Es una cuenta abierta: ciérrala desde Mesas cuando esté pagada.");

            var stateMachine = OrderStateMachineFactory.Create(order.BusinessType, order.OrderType);

            if (!stateMachine.GetAllowedTransitions(order.Status).Contains(request.NewStatus))
                return Result.Failure(
                    $"No se puede cambiar de '{order.Status}' a '{request.NewStatus}'.");

            if (request.NewStatus == OrderStatus.Cancelled)
            {
                // Same rules as CancelOrderCommand: cancellation event, payment guard and inventory reversal.
                order.Cancel(stateMachine, _currentUserService.UserName);
                await _orderInventoryService.ReverseConsumptionForOrderAsync(order, null, cancellationToken);
            }
            else
            {
                order.TransitionTo(request.NewStatus, stateMachine, _currentUserService.UserName);
                await _orderInventoryService.ApplyConsumptionForOrderAsync(order, cancellationToken);

                // A counter sale paid while the kitchen prepared it is handed over once it is ready.
                if (request.NewStatus == OrderStatus.Ready)
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
