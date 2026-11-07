using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Domain.Orders.StateMachines;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders.Commands;

/// <summary>
/// The customer cancels their order of the online menu with the link of its receipt (Fase 29R, ADR 0072), while the business
/// has not accepted it and nothing was paid. A link unknown, changed, expired or of a business that is not active answers
/// as not found.
/// </summary>
[AllowAnonymousRequest("Cancelar con el enlace imposible de adivinar de su recibo un pedido en línea que el negocio no ha aceptado.")]
public record CancelPublicOrderCommand(string Token) : IRequest<Result<PublicOrderTrackingDto>>;

public class CancelPublicOrderCommandValidator : AbstractValidator<CancelPublicOrderCommand>
{
    public CancelPublicOrderCommandValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(100);
    }
}

public class CancelPublicOrderCommandHandler : IRequestHandler<CancelPublicOrderCommand, Result<PublicOrderTrackingDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IOrderInventoryService _inventory;
    private readonly IDateTimeProvider _clock;

    public CancelPublicOrderCommandHandler(
        IApplicationDbContext context, ICurrentTenantService tenantService, IOrderInventoryService inventory, IDateTimeProvider clock)
    {
        _context = context;
        _tenantService = tenantService;
        _inventory = inventory;
        _clock = clock;
    }

    public async Task<Result<PublicOrderTrackingDto>> Handle(CancelPublicOrderCommand request, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var found = await PublicOrderLink.FindAsync(_context, request.Token, now, cancellationToken);
        if (found is null)
            return Result.Failure<PublicOrderTrackingDto>(CustomerOrderCancellation.NotFoundMessage);

        _tenantService.SetTenant(found.Value.TenantId);
        await CustomerOrderCancellation.CancelAsync(_context, _inventory, found.Value.Id, cancellationToken);
        return Result.Success((await PublicOrderLink.ReadAsync(_context, found.Value.Id, now, cancellationToken))!);
    }
}

/// <summary>
/// The customer cancels from their account one of its orders of the online menu (Fase 29R, ADR 0072), with the same rule as
/// the link. Only an order of the account itself; any other is not found, whatever business it is in.
/// </summary>
[RequiresAuthentication]
public record CancelMyOrderCommand(Guid Id) : IRequest<Result<MyOrderDetailDto>>;

public class CancelMyOrderCommandHandler : IRequestHandler<CancelMyOrderCommand, Result<MyOrderDetailDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUser;
    private readonly IOrderInventoryService _inventory;
    private readonly IDateTimeProvider _clock;

    public CancelMyOrderCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUser,
        IOrderInventoryService inventory,
        IDateTimeProvider clock)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUser = currentUser;
        _inventory = inventory;
        _clock = clock;
    }

    public async Task<Result<MyOrderDetailDto>> Handle(CancelMyOrderCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUser.UserId, out var userId))
            return Result.Failure<MyOrderDetailDto>(CustomerOrderCancellation.NotFoundMessage);

        var found = await MyOrder.Of(_context, userId, request.Id)
            .Select(o => new { o.Id, o.TenantId })
            .FirstOrDefaultAsync(cancellationToken);
        if (found is null)
            return Result.Failure<MyOrderDetailDto>(CustomerOrderCancellation.NotFoundMessage);

        _tenantService.SetTenant(found.TenantId);
        await CustomerOrderCancellation.CancelAsync(_context, _inventory, found.Id, cancellationToken);
        return Result.Success((await MyOrder.ReadAsync(_context, userId, found.Id, _clock.UtcNow, cancellationToken))!);
    }
}

/// <summary>
/// Its customer cancels an order of the business in context (Fase 29R): the domain decides whether they may, and what the
/// order took from the inventory goes back, as when the business cancels it (nothing was prepared before it was accepted).
/// </summary>
internal static class CustomerOrderCancellation
{
    public const string NotFoundMessage = "No encontramos ese pedido.";

    public static async Task CancelAsync(
        IApplicationDbContext context, IOrderInventoryService inventory, Guid orderId, CancellationToken cancellationToken)
    {
        var order = await context.Orders
            .Include(o => o.StatusHistory)
            .Include(o => o.ServicePieces)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new EntityNotFoundException("Order", orderId);

        order.CancelByCustomer(OrderStateMachineFactory.Create(order.BusinessType, order.OrderType));
        await inventory.ReverseConsumptionForOrderAsync(order, order.CancellationReason, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }
}
