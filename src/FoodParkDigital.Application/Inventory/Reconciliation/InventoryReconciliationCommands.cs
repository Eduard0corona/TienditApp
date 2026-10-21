using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Catalog;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Inventory.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Inventory.Reconciliation;

/// <summary>One item to reconcile and the difference that was reviewed for it.</summary>
public record InventoryReconciliationLineRequest
{
    public Guid SupplyItemId { get; init; }
    /// <summary>Stock minus history, as the review showed it.</summary>
    public decimal ExpectedDifference { get; init; }
}

/// <summary>
/// Cuadrar el historial (Fase 27C, ADR 0039): records, in the history of each item of a location, what it was missing to
/// add up to the stock, without moving the stock. Up to two movements of the reason LedgerReconciliation: an
/// AdjustmentIncrease with what the history was missing and an AdjustmentDecrease with what it had too much of, each line
/// with the stock before and after, unchanged, and no cost.
/// </summary>
[RequiresPermission(TenantPermission.ManageInventory)]
[RequiresCapability(TenantCapability.Inventory)]
public record ReconcileInventoryLedgerCommand : IRequest<Result<InventoryReconciliationResultDto>>, ITransactionalRequest
{
    public const int MaxLines = 200;
    /// <summary>The largest quantity a stock or a movement line holds (decimal(18,4)).</summary>
    public const decimal MaxDifference = 99_999_999_999_999.9999m;

    public Guid InventoryLocationId { get; init; }
    public List<InventoryReconciliationLineRequest> Lines { get; init; } = [];
}

public class ReconcileInventoryLedgerCommandValidator : AbstractValidator<ReconcileInventoryLedgerCommand>
{
    public ReconcileInventoryLedgerCommandValidator()
    {
        RuleFor(x => x.InventoryLocationId).NotEmpty().WithMessage("Elige la ubicación que vas a cuadrar.");
        RuleFor(x => x.Lines).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Elige al menos un artículo.")
            .Must(lines => lines.Count <= ReconcileInventoryLedgerCommand.MaxLines)
            .WithMessage($"Se cuadran hasta {ReconcileInventoryLedgerCommand.MaxLines} artículos a la vez.")
            .Must(lines => lines.All(l => l is not null)).WithMessage("Falta un artículo en la lista.")
            .Must(lines => lines.Select(l => l.SupplyItemId).Distinct().Count() == lines.Count)
            .WithMessage("Un artículo va una sola vez.");
        RuleForEach(x => x.Lines).Where(l => l is not null).ChildRules(line =>
        {
            line.RuleFor(l => l.SupplyItemId).NotEmpty();
            line.RuleFor(l => l.ExpectedDifference)
                .Must(d => d != 0 && Math.Abs(d) <= ReconcileInventoryLedgerCommand.MaxDifference && decimal.Round(d, 4) == d)
                .WithMessage("La diferencia revisada no es válida: vuelve a revisar.");
        });
    }
}

public class ReconcileInventoryLedgerCommandHandler
    : IRequestHandler<ReconcileInventoryLedgerCommand, Result<InventoryReconciliationResultDto>>
{
    /// <summary>The start of the answer when what does not add up changed after it was reviewed (409).</summary>
    public const string ChangedMessagePrefix = "Cambió lo que no cuadra de";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IFolioGenerator _folios;
    private readonly IInventoryLocationLock _locationLock;

    public ReconcileInventoryLedgerCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenant,
        ICurrentUserService currentUser,
        IDateTimeProvider clock,
        IFolioGenerator folios,
        IInventoryLocationLock locationLock)
    {
        _context = context;
        _tenant = tenant;
        _currentUser = currentUser;
        _clock = clock;
        _folios = folios;
        _locationLock = locationLock;
    }

    public async Task<Result<InventoryReconciliationResultDto>> Handle(ReconcileInventoryLedgerCommand request, CancellationToken cancellationToken)
    {
        // In line with the counts and transfers of the location, and with another reconciliation of it: the second of two
        // at once finds the first one's work done. Then in line with the movements of the day, which take the folio
        // counter in their transaction (sales do not take the lock of the location): what is compared next is not a
        // sale halfway, and no sale comes in between the comparison and the reconciliation.
        await _locationLock.AcquireAsync(_tenant.TenantId, request.InventoryLocationId, cancellationToken);
        var now = _clock.UtcNow;
        await _folios.HoldInventoryMovementNumbersAsync(_tenant.TenantId, now, cancellationToken);

        var location = await _context.InventoryLocations.AsNoTracking()
            .Where(l => l.Id == request.InventoryLocationId)
            .Select(l => new { l.Id, l.Name })
            .FirstOrDefaultAsync(cancellationToken);
        if (location is null)
            return Result.Failure<InventoryReconciliationResultDto>("La ubicación no existe.");

        var supplyIds = request.Lines.Select(l => l.SupplyItemId).ToList();
        var supplies = await _context.SupplyItems.AsNoTracking()
            .Where(s => supplyIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name, s.Unit })
            .ToDictionaryAsync(s => s.Id, cancellationToken);
        if (supplies.Count != supplyIds.Count)
            return Result.Failure<InventoryReconciliationResultDto>("Uno o más artículos no existen.");

        var compared = (await InventoryReconciliationReads.CompareAsync(_context, location.Id, supplyIds, cancellationToken))
            .ToDictionary(c => c.SupplyItemId);
        var toRecord = new List<StockAgainstHistory>();
        var alreadyAddedUp = 0;
        var changed = new List<string>();
        foreach (var line in request.Lines.OrderBy(l => supplies[l.SupplyItemId].Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var current = compared.GetValueOrDefault(line.SupplyItemId);
            var difference = current?.Difference ?? 0;
            if (difference == 0)
                alreadyAddedUp++;
            else if (difference != line.ExpectedDifference)
                changed.Add($"{supplies[line.SupplyItemId].Name} (ahora {Signed(difference, supplies[line.SupplyItemId].Unit)})");
            else
                toRecord.Add(current!);
        }
        if (changed.Count > 0)
            return Result.Failure<InventoryReconciliationResultDto>(
                $"{ChangedMessagePrefix}: {string.Join(", ", changed)}. Vuelve a revisar antes de cuadrar.");

        var movements = new List<InventoryMovement>();
        foreach (var (type, gaps) in new[]
                 {
                     (InventoryMovementType.AdjustmentIncrease, toRecord.Where(r => r.Difference > 0).ToList()),
                     (InventoryMovementType.AdjustmentDecrease, toRecord.Where(r => r.Difference < 0).ToList())
                 })
        {
            if (gaps.Count == 0)
                continue;

            var movement = new InventoryMovement(
                tenantId: _tenant.TenantId,
                movementNumber: await _folios.NextInventoryMovementNumberAsync(_tenant.TenantId, now, cancellationToken),
                movementType: type,
                movementReason: InventoryMovementReason.LedgerReconciliation,
                movementDate: now,
                notes: "Cuadre del historial: la existencia no cambia.");
            foreach (var gap in gaps)
            {
                var unit = supplies[gap.SupplyItemId].Unit;
                movement.AddReconciliationLine(
                    _tenant.TenantId, gap.SupplyItemId, location.Id, Math.Abs(gap.Difference), gap.OnHandQuantity,
                    $"Existencia {UnitTypeRules.FormatQuantity(gap.OnHandQuantity, unit)}; el historial sumaba {UnitTypeRules.FormatQuantity(gap.HistoryQuantity, unit)}");
            }
            _context.InventoryMovements.Add(movement);
            movements.Add(movement);
        }

        if (movements.Count > 0)
        {
            _context.AuditLogs.Add(new AuditLog(
                tenantId: _tenant.TenantId,
                entityName: nameof(InventoryLocation),
                entityId: location.Id.ToString(),
                action: "InventoryLedgerReconciled",
                performedBy: _currentUser.UserName ?? "system",
                metadata: JsonSerializer.Serialize(new
                {
                    Location = location.Name,
                    Movements = movements.Select(m => m.MovementNumber).ToList(),
                    Items = toRecord.Select(r => new { r.SupplyItemId, r.OnHandQuantity, r.HistoryQuantity, r.Difference }).ToList()
                }),
                timestamp: now));
            await _context.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(new InventoryReconciliationResultDto
        {
            ReconciledCount = toRecord.Count,
            AlreadyAddedUpCount = alreadyAddedUp,
            Movements = movements
                .Select(m => new InventoryReconciliationMovementDto { Id = m.Id, MovementNumber = m.MovementNumber, MovementType = m.MovementType })
                .ToList()
        });
    }

    private static string Signed(decimal quantity, Domain.Catalog.Enums.UnitType unit)
        => (quantity > 0 ? "+" : "−") + UnitTypeRules.FormatQuantity(Math.Abs(quantity), unit);
}
