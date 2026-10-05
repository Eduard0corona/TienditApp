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

/// <summary>A draft goes to the supplier: from now on it waits for the goods.</summary>
[RequiresPermission(TenantPermission.ManageInventory)]
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
    private readonly ICurrentTenantService _tenantService;

    public SendPurchaseOrderCommandHandler(IApplicationDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<Result<PurchaseOrderDto>> Handle(SendPurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        var tenantCapabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(_context, _tenantService.TenantId, cancellationToken);
        if (!TenantCapabilityRules.CanAccessInventory(tenantCapabilities))
            return Result.Failure<PurchaseOrderDto>("El negocio no tiene habilitado el módulo de inventario/compras.");

        var purchaseOrder = await _context.PurchaseOrders
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == request.PurchaseOrderId, cancellationToken)
            ?? throw new EntityNotFoundException("PurchaseOrder", request.PurchaseOrderId);

        purchaseOrder.MarkAsSent();
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(await PurchaseOrderReader.LoadAsync(_context, purchaseOrder.Id, cancellationToken));
    }
}
