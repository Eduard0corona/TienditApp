using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Purchasing.DTOs;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Purchasing.Commands;

/// <summary>A draft goes to the supplier: from now on it waits for the goods.</summary>
[RequiresPermission(TenantPermission.ManageInventory)]
[RequiresCapability(TenantCapability.Inventory)]
public record SendPurchaseOrderCommand(Guid PurchaseOrderId) : IRequest<Result<PurchaseOrderDto>>;

public class SendPurchaseOrderCommandValidator : AbstractValidator<SendPurchaseOrderCommand>
{
    public SendPurchaseOrderCommandValidator()
    {
        RuleFor(x => x.PurchaseOrderId).NotEmpty();
    }
}

public class SendPurchaseOrderCommandHandler : IRequestHandler<SendPurchaseOrderCommand, Result<PurchaseOrderDto>>
{
    private readonly IApplicationDbContext _context;

    public SendPurchaseOrderCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<PurchaseOrderDto>> Handle(SendPurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        var purchaseOrder = await _context.PurchaseOrders
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == request.PurchaseOrderId, cancellationToken)
            ?? throw new EntityNotFoundException("PurchaseOrder", request.PurchaseOrderId);

        purchaseOrder.MarkAsSent();
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(await PurchaseOrderReader.LoadAsync(_context, purchaseOrder.Id, cancellationToken));
    }
}
