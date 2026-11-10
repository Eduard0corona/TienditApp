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

        // Another request can complete, cancel or take the piece between this read and this save: the save is caught by
        // the order's row version and the request is decided again with what the other one saved.
        for (var attempt = 1; attempt <= ServiceBoardPieceSaves.MaxAttempts; attempt++)
        {
            var piece = await _context.ServiceOrderPieces
                .Include(p => p.Order)
                .FirstOrDefaultAsync(p => p.Id == request.PieceId, cancellationToken)
                ?? throw new EntityNotFoundException("ServiceOrderPiece", request.PieceId);

            if (!piece.IsTrackable || piece.Order.OrderType != OrderType.Service)
                return Result.Failure("La pieza no pertenece al flujo de tablero de servicios.");

            if (piece.Status is ServiceOrderPieceStatus.Completed or ServiceOrderPieceStatus.Cancelled)
                return Result.Failure("No se puede asignar una pieza completada o cancelada.");

            var isAdmin = await _access.IsTenantManagerAsync(cancellationToken);

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
        // Another request can complete the piece, or give it to someone else, between this read and this save: the save
        // is caught by the order's row version and the request is decided again with what the other one saved, so who
        // worked a completed piece is never erased by a release read before it closed.
        for (var attempt = 1; attempt <= ServiceBoardPieceSaves.MaxAttempts; attempt++)
        {
            var piece = await _context.ServiceOrderPieces
                .Include(p => p.Order)
                .FirstOrDefaultAsync(p => p.Id == request.PieceId, cancellationToken)
                ?? throw new EntityNotFoundException("ServiceOrderPiece", request.PieceId);

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
/// Saves a change of a piece of the service board. The piece has no concurrency token of its own: assigning, releasing
/// and changing its status go through its order (<see cref="Order.AssignServicePiece"/>,
/// <see cref="Order.ReleaseServicePiece"/> and <see cref="Order.ChangeServicePieceStatus"/>), whose row version catches a
/// change saved by another request meanwhile.
/// </summary>
internal static class ServiceBoardPieceSaves
{
    public const int MaxAttempts = 3;
    public const string SavedMeanwhileMessage =
        "La orden fue actualizada por otro proceso. Refresca el tablero e intenta nuevamente.";

    /// <summary>
    /// Saves, or forgets what this attempt read and returns <see langword="false"/> when the order changed meanwhile, so
    /// the caller reads it again.
    /// </summary>
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
