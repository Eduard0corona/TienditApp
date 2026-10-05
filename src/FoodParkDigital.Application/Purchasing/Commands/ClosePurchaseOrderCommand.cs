using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Purchasing.DTOs;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Purchasing.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Purchasing.Commands;

/// <summary>
/// Part of the order arrived and the rest will not: it closes with what came in. Stock and spend do not change (they
/// already moved with each reception); the order keeps what was asked for and why the rest is no longer expected.
/// </summary>
[RequiresPermission(TenantPermission.ManageInventory)]
[RequiresCapability(TenantCapability.Inventory)]
public record ClosePurchaseOrderCommand(Guid PurchaseOrderId, string? Reason) : IRequest<Result<PurchaseOrderDto>>;

public class ClosePurchaseOrderCommandValidator : AbstractValidator<ClosePurchaseOrderCommand>
{
    public ClosePurchaseOrderCommandValidator()
    {
        RuleFor(x => x.PurchaseOrderId).NotEmpty();
        RuleFor(x => x.Reason)
            .MaximumLength(PurchaseOrder.MaxCloseReasonLength)
            .WithMessage($"El motivo admite hasta {PurchaseOrder.MaxCloseReasonLength} caracteres.");
    }
}

public class ClosePurchaseOrderCommandHandler : IRequestHandler<ClosePurchaseOrderCommand, Result<PurchaseOrderDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _clock;

    public ClosePurchaseOrderCommandHandler(IApplicationDbContext context, IDateTimeProvider clock)
    {
        _context = context;
        _clock = clock;
    }

    public async Task<Result<PurchaseOrderDto>> Handle(ClosePurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        var purchaseOrder = await _context.PurchaseOrders
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == request.PurchaseOrderId, cancellationToken)
            ?? throw new EntityNotFoundException("PurchaseOrder", request.PurchaseOrderId);

        purchaseOrder.CloseWithoutRemainder(request.Reason, _clock.UtcNow);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(await PurchaseOrderReader.LoadAsync(_context, purchaseOrder.Id, cancellationToken));
    }
}
