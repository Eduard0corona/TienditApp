using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.StateMachines;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders.Commands;

public record UpdateServiceOrderPieceStatusCommand : IRequest<Result>
{
    public Guid OrderId { get; init; }
    public Guid PieceId { get; init; }
    public ServiceOrderPieceStatus NewStatus { get; init; }
}

public class UpdateServiceOrderPieceStatusCommandValidator : AbstractValidator<UpdateServiceOrderPieceStatusCommand>
{
    public UpdateServiceOrderPieceStatusCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.PieceId).NotEmpty();
        RuleFor(x => x.NewStatus).IsInEnum();
    }
}

public class UpdateServiceOrderPieceStatusCommandHandler : IRequestHandler<UpdateServiceOrderPieceStatusCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public UpdateServiceOrderPieceStatusCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result> Handle(UpdateServiceOrderPieceStatusCommand request, CancellationToken cancellationToken)
    {
        var order = await _context.Orders
            .Include(o => o.ServicePieces)
            .Include(o => o.StatusHistory)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken)
            ?? throw new EntityNotFoundException("Order", request.OrderId);

        if (order.OrderType != OrderType.Service)
            return Result.Failure("La orden indicada no es de tipo servicio.");

        var piece = order.ServicePieces.FirstOrDefault(x => x.Id == request.PieceId);
        if (piece is null)
            return Result.Failure("La pieza indicada no existe dentro de la orden.");

        if (piece.Status == request.NewStatus)
            return Result.Success();

        try
        {
            piece.UpdateStatus(request.NewStatus);
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.Message);
        }

        SyncOrderStatusFromPieces(order, _currentUserService.UserName);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static void SyncOrderStatusFromPieces(Order order, string? changedBy)
    {
        var stateMachine = OrderStateMachineFactory.Create(order.BusinessType, order.OrderType);
        var pieces = order.ServicePieces.ToList();

        if (pieces.Count == 0)
            return;

        var allowed = stateMachine.GetAllowedTransitions(order.Status);
        var allTerminal = pieces.All(x =>
            x.Status is ServiceOrderPieceStatus.Delivered or ServiceOrderPieceStatus.Cancelled);
        var anyInProgress = pieces.Any(x =>
            x.Status is ServiceOrderPieceStatus.Inspection
                or ServiceOrderPieceStatus.Washing
                or ServiceOrderPieceStatus.Drying
                or ServiceOrderPieceStatus.Detailing);
        var anyAccepted = pieces.Any(x => x.Status is not ServiceOrderPieceStatus.Received and not ServiceOrderPieceStatus.Cancelled);

        if (allTerminal && allowed.Contains(OrderStatus.Completed))
        {
            order.TransitionTo(OrderStatus.Completed, stateMachine, changedBy);
            return;
        }

        if (anyInProgress && allowed.Contains(OrderStatus.InProgress))
        {
            order.TransitionTo(OrderStatus.InProgress, stateMachine, changedBy);
            return;
        }

        if (anyAccepted && allowed.Contains(OrderStatus.Accepted))
            order.TransitionTo(OrderStatus.Accepted, stateMachine, changedBy);
    }
}
