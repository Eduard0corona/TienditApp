using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Events;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders.Commands;

[RequiresPermission(TenantPermission.ManageOrders)]
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
    private readonly IOrderInventoryService _orderInventoryService;

    public UpdateServiceOrderPieceStatusCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        IOrderInventoryService? orderInventoryService = null)
    {
        _context = context;
        _currentUserService = currentUserService;
        _orderInventoryService = orderInventoryService ?? NoOpOrderInventoryService.Instance;
    }

    public async Task<Result> Handle(UpdateServiceOrderPieceStatusCommand request, CancellationToken cancellationToken)
    {
        // Two employees can change pieces of the same order at the same time. Every change also updates
        // the order row, so the second save hits the order's row version and is retried with fresh data.
        const int maxAttempts = 3;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var order = await _context.Orders
                .Include(o => o.ServicePieces)
                .Include(o => o.StatusHistory)
                .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken)
                ?? throw new EntityNotFoundException("Order", request.OrderId);

            if (order.Status == OrderStatus.Cancelled)
                return Result.Failure("La orden esta cancelada; sus piezas ya no se pueden modificar.");

            var piece = order.ServicePieces.FirstOrDefault(x => x.Id == request.PieceId);
            if (piece is null)
                return Result.Failure("La pieza indicada no existe dentro de la orden.");

            if (piece.Status == request.NewStatus)
                return Result.Success();

            var applied = await ApplyChangeAsync(order, piece, request, cancellationToken);
            if (applied.IsFailure)
                return applied;

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
            "La orden fue actualizada por otro proceso. Refresca el tablero e intenta nuevamente.");
    }

    private async Task<Result> ApplyChangeAsync(
        Order order,
        ServiceOrderPiece piece,
        UpdateServiceOrderPieceStatusCommand request,
        CancellationToken cancellationToken)
    {
        var isAdmin =
            _currentUserService.Role is "TENANT_ADMIN" or "SUPER_ADMIN"
            || _currentUserService.TenantRole is "OWNER" or "ADMIN";
        var hasCurrentUser = Guid.TryParse(_currentUserService.UserId, out var currentUserId);

        if (!hasCurrentUser && !isAdmin)
            return Result.Failure("No se pudo identificar el usuario actual.");

        if (piece.AssignedToUserId.HasValue
            && (!hasCurrentUser || piece.AssignedToUserId.Value != currentUserId)
            && !isAdmin)
        {
            return Result.Failure("La pieza esta asignada a otro empleado.");
        }

        if (request.NewStatus == ServiceOrderPieceStatus.Cancelled && !isAdmin)
            return Result.Failure("Solo un administrador puede cancelar piezas de servicio.");

        var now = DateTimeOffset.UtcNow;
        if (!piece.AssignedToUserId.HasValue && hasCurrentUser)
            piece.AssignTo(currentUserId, _currentUserService.UserName, now);

        var previousStatus = piece.Status;

        try
        {
            order.ChangeServicePieceStatus(piece, request.NewStatus, now);
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.Message);
        }

        piece.AddDomainEvent(new ServiceOrderPieceStatusChangedEvent(
            request.OrderId,
            request.PieceId,
            order.TenantId,
            previousStatus,
            request.NewStatus));

        // Inventory is consumed in the same unit of work as the status change: if stock is missing the
        // whole change fails instead of leaving a completed piece without its consumption.
        if (request.NewStatus == ServiceOrderPieceStatus.Completed)
            await _orderInventoryService.ApplyConsumptionForServicePieceAsync(order, piece, cancellationToken);

        // When the pieces move the order (e.g. to Completed), the rest of the order (products sold with
        // the service) is consumed too; the call is idempotent and only acts on consuming statuses.
        if (SyncOrderStatusFromPieces(order, _currentUserService.UserName))
            await _orderInventoryService.ApplyConsumptionForOrderAsync(order, cancellationToken);

        return Result.Success();
    }

    private static bool SyncOrderStatusFromPieces(Order order, string? changedBy)
    {
        var stateMachine = OrderStateMachineFactory.Create(order.BusinessType, order.OrderType);
        var pieces = order.ServicePieces
            .Where(x => x.IsTrackable)
            .ToList();

        if (pieces.Count == 0)
            return false;

        var allowed = stateMachine.GetAllowedTransitions(order.Status);
        var nonCancelled = pieces
            .Where(x => x.Status != ServiceOrderPieceStatus.Cancelled)
            .ToList();
        var allCompleted = nonCancelled.Count > 0
            && nonCancelled.All(x => x.Status == ServiceOrderPieceStatus.Completed);
        var anyInProgress = pieces.Any(x => x.Status == ServiceOrderPieceStatus.InProgress);
        var anyAccepted = pieces.Any(x => x.Status == ServiceOrderPieceStatus.Accepted);

        if (allCompleted && allowed.Contains(OrderStatus.Completed))
        {
            order.TransitionTo(OrderStatus.Completed, stateMachine, changedBy);
            return true;
        }

        if (anyInProgress && allowed.Contains(OrderStatus.InProgress))
        {
            order.TransitionTo(OrderStatus.InProgress, stateMachine, changedBy);
            return true;
        }

        if (anyAccepted && allowed.Contains(OrderStatus.Accepted))
        {
            order.TransitionTo(OrderStatus.Accepted, stateMachine, changedBy);
            return true;
        }

        return false;
    }
}
