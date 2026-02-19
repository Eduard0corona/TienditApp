using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders.Commands;

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

    public AssignServiceOrderPieceToMeCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result> Handle(AssignServiceOrderPieceToMeCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUserService.UserId, out var userId))
            return Result.Failure("No se pudo identificar el usuario actual.");

        var piece = await _context.ServiceOrderPieces
            .Include(p => p.Order)
            .FirstOrDefaultAsync(p => p.Id == request.PieceId, cancellationToken)
            ?? throw new EntityNotFoundException("ServiceOrderPiece", request.PieceId);

        if (!piece.IsTrackable || piece.Order.OrderType != OrderType.Service)
            return Result.Failure("La pieza no pertenece al flujo de tablero de servicios.");

        if (piece.Status is ServiceOrderPieceStatus.Completed or ServiceOrderPieceStatus.Cancelled)
            return Result.Failure("No se puede asignar una pieza completada o cancelada.");

        var isAdmin =
            _currentUserService.Role is "TENANT_ADMIN" or "SUPER_ADMIN"
            || _currentUserService.TenantRole is "OWNER" or "ADMIN";

        if (piece.AssignedToUserId.HasValue && piece.AssignedToUserId.Value == userId)
            return Result.Success();

        if (piece.AssignedToUserId.HasValue && !isAdmin)
            return Result.Failure("La pieza ya esta asignada a otro empleado.");

        piece.AssignTo(userId, _currentUserService.UserName, DateTimeOffset.UtcNow);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

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

    public ReleaseServiceOrderPieceAssignmentCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result> Handle(ReleaseServiceOrderPieceAssignmentCommand request, CancellationToken cancellationToken)
    {
        var piece = await _context.ServiceOrderPieces
            .Include(p => p.Order)
            .FirstOrDefaultAsync(p => p.Id == request.PieceId, cancellationToken)
            ?? throw new EntityNotFoundException("ServiceOrderPiece", request.PieceId);

        if (!piece.IsTrackable || piece.Order.OrderType != OrderType.Service)
            return Result.Failure("La pieza no pertenece al flujo de tablero de servicios.");

        if (piece.AssignedToUserId is null)
            return Result.Success();

        var isAdmin =
            _currentUserService.Role is "TENANT_ADMIN" or "SUPER_ADMIN"
            || _currentUserService.TenantRole is "OWNER" or "ADMIN";
        var isCurrentAssignee = Guid.TryParse(_currentUserService.UserId, out var userId)
            && piece.AssignedToUserId == userId;

        if (!isAdmin && !isCurrentAssignee)
            return Result.Failure("Solo el asignado o un administrador pueden liberar la pieza.");

        piece.ReleaseAssignment();
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
