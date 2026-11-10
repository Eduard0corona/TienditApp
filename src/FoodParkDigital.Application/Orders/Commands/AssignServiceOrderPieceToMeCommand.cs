using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders.Commands;

[RequiresPermission(TenantPermission.ManageServiceBoard)]
[RequiresCapability(TenantCapability.Service)]
public record AssignServiceOrderPieceToMeCommand : IRequest<Result>
{
    public Guid PieceId { get; init; }
}

public class AssignServiceOrderPieceToMeCommandValidator : AbstractValidator<AssignServiceOrderPieceToMeCommand>
{
    public AssignServiceOrderPieceToMeCommandValidator()
    {
        RuleFor(x => x.PieceId).NotEmpty();
    }
}

public class AssignServiceOrderPieceToMeCommandHandler : IRequestHandler<AssignServiceOrderPieceToMeCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly CurrentAccessContext _access;
    private readonly IOrderNotificationService _orderNotificationService;

    public AssignServiceOrderPieceToMeCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        CurrentAccessContext access,
        IOrderNotificationService orderNotificationService)
    {
        _context = context;
        _currentUserService = currentUserService;
        _access = access;
        _orderNotificationService = orderNotificationService;
    }

    public async Task<Result> Handle(AssignServiceOrderPieceToMeCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUserService.UserId, out var userId))
            return Result.Failure("No se pudo identificar el usuario actual.");

        var isAdmin = await _access.IsTenantManagerAsync(cancellationToken);

        for (var attempt = 1; attempt <= ServiceBoardPieceSaves.MaxAttempts; attempt++)
        {
            var piece = await ServiceBoardPieceSaves.LoadAsync(_context, request.PieceId, cancellationToken);

            if (!piece.IsTrackable || piece.Order.OrderType != OrderType.Service)
                return Result.Failure("La pieza no pertenece al flujo de tablero de servicios.");

            if (piece.Status is ServiceOrderPieceStatus.Completed or ServiceOrderPieceStatus.Cancelled)
                return Result.Failure("No se puede asignar una pieza completada o cancelada.");

            if (piece.AssignedToUserId.HasValue && piece.AssignedToUserId.Value == userId)
                return Result.Success();

            if (piece.AssignedToUserId.HasValue && !isAdmin)
                return Result.Failure("La pieza ya esta asignada a otro empleado.");

            piece.Order.AssignServicePiece(piece, userId, _currentUserService.UserName, DateTimeOffset.UtcNow);
            if (!await ServiceBoardPieceSaves.TrySaveAsync(_context, piece.Order, cancellationToken))
                continue;

            await _orderNotificationService.NotifyServiceBoardPieceUpdated(
                piece.Order.TenantId,
                piece.OrderId,
                piece.Id,
                piece.Status,
                piece.AssignedToUserId);

            return Result.Success();
        }

        return Result.Failure(ServiceBoardPieceSaves.SavedMeanwhileMessage);
    }
}

[RequiresPermission(TenantPermission.ManageServiceBoard)]
[RequiresCapability(TenantCapability.Service)]
public record ReleaseServiceOrderPieceAssignmentCommand : IRequest<Result>
{
    public Guid PieceId { get; init; }
}

public class ReleaseServiceOrderPieceAssignmentCommandValidator : AbstractValidator<ReleaseServiceOrderPieceAssignmentCommand>
{
    public ReleaseServiceOrderPieceAssignmentCommandValidator()
    {
        RuleFor(x => x.PieceId).NotEmpty();
    }
}

public class ReleaseServiceOrderPieceAssignmentCommandHandler : IRequestHandler<ReleaseServiceOrderPieceAssignmentCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly CurrentAccessContext _access;
    private readonly IOrderNotificationService _orderNotificationService;

    public ReleaseServiceOrderPieceAssignmentCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        CurrentAccessContext access,
        IOrderNotificationService orderNotificationService)
    {
        _context = context;
        _currentUserService = currentUserService;
        _access = access;
        _orderNotificationService = orderNotificationService;
    }

    public async Task<Result> Handle(ReleaseServiceOrderPieceAssignmentCommand request, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= ServiceBoardPieceSaves.MaxAttempts; attempt++)
        {
            var piece = await ServiceBoardPieceSaves.LoadAsync(_context, request.PieceId, cancellationToken);

            if (!piece.IsTrackable || piece.Order.OrderType != OrderType.Service)
                return Result.Failure("La pieza no pertenece al flujo de tablero de servicios.");

            if (piece.Status is ServiceOrderPieceStatus.Completed or ServiceOrderPieceStatus.Cancelled)
                return Result.Failure("No se puede liberar una pieza completada o cancelada.");

            if (piece.AssignedToUserId is null)
                return Result.Success();

            var isAdmin = await _access.IsTenantManagerAsync(cancellationToken);
            var isCurrentAssignee = Guid.TryParse(_currentUserService.UserId, out var userId)
                && piece.AssignedToUserId == userId;

            if (!isAdmin && !isCurrentAssignee)
                return Result.Failure("Solo el asignado o un administrador pueden liberar la pieza.");

            piece.Order.ReleaseServicePiece(piece, DateTimeOffset.UtcNow);
            if (!await ServiceBoardPieceSaves.TrySaveAsync(_context, piece.Order, cancellationToken))
                continue;

            await _orderNotificationService.NotifyServiceBoardPieceUpdated(
                piece.Order.TenantId,
                piece.OrderId,
                piece.Id,
                piece.Status,
                piece.AssignedToUserId);

            return Result.Success();
        }

        return Result.Failure(ServiceBoardPieceSaves.SavedMeanwhileMessage);
    }
}

/// <summary>
/// Taking or releasing a piece of the service board writes its order row, like changing the piece's status
/// (<see cref="Order.ChangeServicePieceStatus"/>): when someone completes, cancels, takes or releases the piece
/// meanwhile, the save hits the order's row version and the use case decides again with what they saved, instead of
/// writing over it (a release that erased who worked a piece completed meanwhile, or two employees who both think they
/// took it).
/// </summary>
internal static class ServiceBoardPieceSaves
{
    public const int MaxAttempts = 3;
    public const string SavedMeanwhileMessage = "La orden fue actualizada por otro proceso. Refresca el tablero e intenta nuevamente.";

    public static async Task<ServiceOrderPiece> LoadAsync(IApplicationDbContext context, Guid pieceId, CancellationToken cancellationToken)
        => await context.ServiceOrderPieces
            .Include(p => p.Order)
            .FirstOrDefaultAsync(p => p.Id == pieceId, cancellationToken)
            ?? throw new EntityNotFoundException("ServiceOrderPiece", pieceId);

    /// <summary>Saves, or forgets what was loaded when the order changed meanwhile, so the next attempt reads it again.</summary>
    public static async Task<bool> TrySaveAsync(IApplicationDbContext context, Order order, CancellationToken cancellationToken)
    {
        // The order row is written even when its time did not change (two changes in the same tick), so its row version
        // is always checked.
        context.ChangeTracker.Entries<Order>().Single(entry => ReferenceEquals(entry.Entity, order))
            .Property(o => o.UpdatedAt).IsModified = true;
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            context.ChangeTracker.Clear();
            return false;
        }
    }
}
