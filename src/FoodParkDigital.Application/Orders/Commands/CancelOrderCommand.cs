using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders.Commands;

[RequiresPermission(TenantPermission.ManageOrders)]
public record CancelOrderCommand : IRequest<Result>
{
    public Guid OrderId { get; init; }
    public string? Reason { get; init; }

    /// <summary>
    /// Whether what the order consumed goes back to the stock (true by default). A dish of a platform's order is often
    /// already cooked when its app cancels it: then nothing goes back.
    /// </summary>
    public bool RestockItems { get; init; } = true;
}

public class CancelOrderCommandValidator : AbstractValidator<CancelOrderCommand>
{
    public CancelOrderCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
    }
}

public class CancelOrderCommandHandler : IRequestHandler<CancelOrderCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IOrderInventoryService _orderInventoryService;

    public CancelOrderCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        IOrderInventoryService orderInventoryService)
    {
        _context = context;
        _currentUserService = currentUserService;
        _orderInventoryService = orderInventoryService;
    }

    public async Task<Result> Handle(CancelOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _context.Orders
            .Include(o => o.StatusHistory)
            .Include(o => o.ServicePieces)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken)
            ?? throw new EntityNotFoundException("Order", request.OrderId);

        var stateMachine = OrderStateMachineFactory.Create(order.BusinessType, order.OrderType);
        order.Cancel(stateMachine, _currentUserService.UserName, request.Reason);
        if (request.RestockItems)
            await _orderInventoryService.ReverseConsumptionForOrderAsync(order, request.Reason, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
