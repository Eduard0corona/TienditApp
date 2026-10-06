using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Purchasing.DTOs;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Inventory.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Purchasing.Commands;

public record ReceivePurchaseOrderLineRequest
{
    public Guid PurchaseOrderLineId { get; init; }
    public decimal QuantityReceived { get; init; }
    /// <summary>The supplier's actual price when it differs from the ordered one; the stock is valued at it.</summary>
    public decimal? UnitCost { get; init; }
}

[RequiresPermission(TenantPermission.ManageInventory)]
[RequiresCapability(TenantCapability.Inventory)]
public record ReceivePurchaseOrderCommand : IRequest<Result<PurchaseOrderDto>>, ITransactionalRequest
{
    public Guid PurchaseOrderId { get; init; }
    public DateTimeOffset? ReceivedAt { get; init; }
    public List<ReceivePurchaseOrderLineRequest> Lines { get; init; } = [];
    /// <summary>
    /// Identifies this reception: the same key again (a double click, a retry after a timeout) answers with the
    /// purchase order as it is, without receiving twice. The app sends a new one for each reception.
    /// </summary>
    public Guid? IdempotencyKey { get; init; }
}

public class ReceivePurchaseOrderCommandValidator : AbstractValidator<ReceivePurchaseOrderCommand>
{
    public ReceivePurchaseOrderCommandValidator()
    {
        RuleFor(x => x.PurchaseOrderId).NotEmpty();
        RuleFor(x => x.IdempotencyKey)
            .NotEqual(Guid.Empty)
            .When(x => x.IdempotencyKey.HasValue)
            .WithMessage("La clave de la recepción no es válida.");
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Debes agregar al menos una línea recibida.");
        // A repeated line would add stock twice while the purchase order records only the last quantity.
        RuleFor(x => x.Lines)
            .Must(lines => lines.Select(l => l.PurchaseOrderLineId).Distinct().Count() == lines.Count)
            .WithMessage("Cada línea de la compra solo puede aparecer una vez por recepción.");
        RuleFor(x => x.ReceivedAt)
            .LessThanOrEqualTo(_ => DateTimeOffset.UtcNow.AddMinutes(5))
            .When(x => x.ReceivedAt.HasValue)
            .WithMessage("La fecha de recepción no puede estar en el futuro.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.PurchaseOrderLineId).NotEmpty();
            line.RuleFor(x => x.QuantityReceived).GreaterThan(0);
            line.RuleFor(x => x.UnitCost)
                .GreaterThanOrEqualTo(0)
                .When(x => x.UnitCost.HasValue)
                .WithMessage("El costo unitario no puede ser negativo.");
        });
    }
}

public class ReceivePurchaseOrderCommandHandler : IRequestHandler<ReceivePurchaseOrderCommand, Result<PurchaseOrderDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IFolioGenerator _folios;

    public ReceivePurchaseOrderCommandHandler(IApplicationDbContext context, ICurrentTenantService tenantService, IFolioGenerator folios)
    {
        _context = context;
        _tenantService = tenantService;
        _folios = folios;
    }

    public async Task<Result<PurchaseOrderDto>> Handle(ReceivePurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        // The movement of a reception with a key is found by it (IX_InventoryMovements_TenantId_ExternalReference is
        // unique): the same reception again was already received.
        var reference = request.IdempotencyKey is { } idempotencyKey ? ReceptionReference(request.PurchaseOrderId, idempotencyKey) : null;
        if (reference is not null && await IsReceivedAsync(reference, cancellationToken))
            return Result.Success(await PurchaseOrderReader.LoadAsync(_context, request.PurchaseOrderId, cancellationToken));

        var purchaseOrder = await _context.PurchaseOrders
            .Include(x => x.Lines)
            .Include(x => x.Supplier)
            .FirstOrDefaultAsync(x => x.Id == request.PurchaseOrderId, cancellationToken)
            ?? throw new EntityNotFoundException("PurchaseOrder", request.PurchaseOrderId);

        var receivedAt = request.ReceivedAt ?? DateTimeOffset.UtcNow;
        var lineMap = purchaseOrder.Lines.ToDictionary(x => x.Id);
        var reception = new Dictionary<Guid, decimal>();
        var actualCosts = new Dictionary<Guid, decimal>();

        foreach (var line in request.Lines)
        {
            if (!lineMap.ContainsKey(line.PurchaseOrderLineId))
                return Result.Failure<PurchaseOrderDto>("Una línea no pertenece a la compra seleccionada.");

            reception[line.PurchaseOrderLineId] = line.QuantityReceived;
            if (line.UnitCost is { } unitCost)
                actualCosts[line.PurchaseOrderLineId] = unitCost;
        }

        purchaseOrder.RegisterReception(reception, receivedAt, actualCosts);

        var affectedSupplyIds = purchaseOrder.Lines.Select(x => x.SupplyItemId).Distinct().ToList();
        var affectedLocationIds = purchaseOrder.Lines.Select(x => x.InventoryLocationId).Distinct().ToList();

        var supplies = await _context.SupplyItems
            .Where(x => affectedSupplyIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var stocks = await _context.InventoryStocks
            .Where(x =>
                affectedSupplyIds.Contains(x.SupplyItemId) &&
                affectedLocationIds.Contains(x.InventoryLocationId))
            .ToListAsync(cancellationToken);

        var stockMap = stocks.ToDictionary(
            x => $"{x.SupplyItemId:N}-{x.InventoryLocationId:N}",
            x => x);

        var onHandBySupply = await _context.InventoryStocks
            .Where(x => affectedSupplyIds.Contains(x.SupplyItemId))
            .GroupBy(x => x.SupplyItemId)
            .Select(g => new { SupplyItemId = g.Key, Quantity = g.Sum(x => x.OnHandQuantity) })
            .ToDictionaryAsync(x => x.SupplyItemId, x => x.Quantity, cancellationToken);

        var movement = new InventoryMovement(
            tenantId: _tenantService.TenantId,
            movementNumber: await _folios.NextInventoryMovementNumberAsync(_tenantService.TenantId, receivedAt, cancellationToken),
            movementType: InventoryMovementType.Entry,
            movementReason: InventoryMovementReason.Purchase,
            movementDate: receivedAt,
            referenceNumber: purchaseOrder.OrderNumber,
            externalReference: reference ?? $"PO:{purchaseOrder.Id:N}:RECV:{DateTime.UtcNow:yyyyMMddHHmmssfff}",
            counterpartyName: purchaseOrder.Supplier.Name,
            notes: $"Recepción de compra {purchaseOrder.OrderNumber}");

        // Stock follows the quantities the purchase order accepted, valued at the cost captured on arrival.
        foreach (var (lineId, quantityReceived) in reception)
        {
            var line = lineMap[lineId];
            if (quantityReceived <= 0)
                continue;

            var key = $"{line.SupplyItemId:N}-{line.InventoryLocationId:N}";
            if (!stockMap.TryGetValue(key, out var stock))
            {
                stock = new InventoryStock(_tenantService.TenantId, line.SupplyItemId, line.InventoryLocationId);
                _context.InventoryStocks.Add(stock);
                stockMap[key] = stock;
            }

            var quantityBefore = stock.OnHandQuantity;
            stock.ApplyMovement(quantityReceived, line.UnitCost, receivedAt);

            var movementLine = movement.AddLine(
                tenantId: _tenantService.TenantId,
                supplyItemId: line.SupplyItemId,
                inventoryLocationId: line.InventoryLocationId,
                quantity: quantityReceived,
                unitCost: line.UnitCost,
                notes: $"Recepción de {purchaseOrder.OrderNumber}");
            movementLine.SetBalances(quantityBefore, stock.OnHandQuantity);

            if (supplies.TryGetValue(line.SupplyItemId, out var supply))
            {
                var currentOnHand = onHandBySupply.GetValueOrDefault(line.SupplyItemId);
                supply.ApplyInboundCost(line.UnitCost, currentOnHand, quantityReceived);
                onHandBySupply[line.SupplyItemId] = currentOnHand + quantityReceived;
            }
        }

        movement.RecalculateTotalAmount();
        _context.InventoryMovements.Add(movement);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (reference is not null)
        {
            // The same reception arrived twice at once and the other request stored it first (its movement, or its
            // change to the purchase order): this copy is undone whole, folio and stock included, and the purchase
            // order as received is the answer.
            await _context.DiscardUnitOfWorkAsync(cancellationToken);
            if (await IsReceivedAsync(reference, cancellationToken))
                return Result.Success(await PurchaseOrderReader.LoadAsync(_context, request.PurchaseOrderId, cancellationToken));
            throw;
        }

        var dto = await PurchaseOrderReader.LoadAsync(_context, purchaseOrder.Id, cancellationToken);
        return Result.Success(dto);
    }

    /// <summary>The external reference of the movement of a reception, unique per business.</summary>
    internal static string ReceptionReference(Guid purchaseOrderId, Guid idempotencyKey)
        => $"PO:{purchaseOrderId:N}:RECV:{idempotencyKey:N}";

    private Task<bool> IsReceivedAsync(string reference, CancellationToken cancellationToken)
        => _context.InventoryMovements.AsNoTracking().AnyAsync(m => m.ExternalReference == reference, cancellationToken);
}
