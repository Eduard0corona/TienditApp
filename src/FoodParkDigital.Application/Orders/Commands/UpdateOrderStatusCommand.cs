using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.StateMachines;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders.Commands;

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

    public UpdateOrderStatusCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result> Handle(UpdateOrderStatusCommand request, CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var order = await _context.Orders
                .Include(o => o.StatusHistory)
                .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken)
                ?? throw new EntityNotFoundException("Order", request.OrderId);

            if (order.Status == request.NewStatus)
                return Result.Success();

            var stateMachine = OrderStateMachineFactory.Create(order.BusinessType);

            if (!stateMachine.GetAllowedTransitions(order.Status).Contains(request.NewStatus))
                return Result.Failure(
                    $"No se puede cambiar de '{order.Status}' a '{request.NewStatus}'.");

            order.TransitionTo(request.NewStatus, stateMachine, _currentUserService.UserName);

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
