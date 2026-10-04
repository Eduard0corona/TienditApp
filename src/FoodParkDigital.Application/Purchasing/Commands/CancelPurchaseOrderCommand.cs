using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Purchasing.DTOs;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Purchasing.Commands;

/// <summary>A draft or a sent order that will not arrive; once goods came in it can no longer be cancelled.</summary>
[RequiresPermission(TenantPermission.ManageInventory)]
public record CancelPurchaseOrderCommand(Guid PurchaseOrderId) : IRequest<Result<PurchaseOrderDto>>;

public class CancelPurchaseOrderCommandValidator : AbstractValidator<CancelPurchaseOrderCommand>
{
    public CancelPurchaseOrderCommandValidator()
    {
        RuleFor(x => x.PurchaseOrderId).NotEmpty();
    }
}

public class CancelPurchaseOrderCommandHandler : IRequestHandler<CancelPurchaseOrderCommand, Result<PurchaseOrderDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public CancelPurchaseOrderCommandHandler(IApplicationDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<Result<PurchaseOrderDto>> Handle(CancelPurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        var tenantCapabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(_context, _tenantService.TenantId, cancellationToken);
        if (!TenantCapabilityRules.CanAccessInventory(tenantCapabilities))
            return Result.Failure<PurchaseOrderDto>("El negocio no tiene habilitado el módulo de inventario/compras.");

        var purchaseOrder = await _context.PurchaseOrders
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == request.PurchaseOrderId, cancellationToken)
            ?? throw new EntityNotFoundException("PurchaseOrder", request.PurchaseOrderId);

        purchaseOrder.Cancel();
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(await PurchaseOrderReader.LoadAsync(_context, purchaseOrder.Id, cancellationToken));
    }
}
