using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Catalog;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Inventory.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Inventory.Transfers;

/// <summary>
/// The two movements of a transfer (Fase 27B, ADR 0038), each with an external reference of the transfer: the unique index
/// of the references keeps a transfer from moving the stock twice, and manual movements cannot use the prefix.
/// </summary>
public static class InventoryTransferPostings
{
    public const string ReferencePrefix = "TRF:";

    public static string OutReference(Guid transferId) => $"{ReferencePrefix}{transferId:N}:OUT";

    public static string InReference(Guid transferId) => $"{ReferencePrefix}{transferId:N}:IN";

    /// <summary>
    /// What one item of a transfer is worth, the same leaving and entering: its average cost; without one, its last cost;
    /// without it, its reference cost. Null when it has none (Fase 27B, ADR 0038).
    /// </summary>
    public static decimal? UnitCost(decimal averageCost, decimal lastCost, decimal defaultCost)
        => averageCost > 0 ? averageCost
            : lastCost > 0 ? lastCost
            : defaultCost > 0 ? defaultCost
            : null;
}

/// <summary>One item to transfer and how much of it.</summary>
public record InventoryTransferLineRequest
{
    public Guid SupplyItemId { get; init; }
    public decimal Quantity { get; init; }
}

/// <summary>
/// Transferir entre ubicaciones (Fase 27B, ADR 0038): the items leave one location and enter the other in the same
/// transaction, all or nothing, as a TransferOut and a TransferIn valued alike. The app sends a key per transfer: the same
/// key again answers the transfer already made, without moving anything.
/// </summary>
[RequiresPermission(TenantPermission.ManageInventory)]
[RequiresCapability(TenantCapability.Inventory)]
public record CreateInventoryTransferCommand : IRequest<Result<InventoryTransferDto>>, ITransactionalRequest
{
    public const decimal MaxQuantity = 9_999_999m;

    public Guid IdempotencyKey { get; init; }
    public Guid FromLocationId { get; init; }
    public Guid ToLocationId { get; init; }
    public string? Notes { get; init; }
    public List<InventoryTransferLineRequest> Lines { get; init; } = [];
}

public class CreateInventoryTransferCommandValidator : AbstractValidator<CreateInventoryTransferCommand>
{
    public CreateInventoryTransferCommandValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty().WithMessage("La transferencia necesita su llave: vuelve a abrirla.");
        RuleFor(x => x.FromLocationId).NotEmpty().WithMessage("Elige de qué ubicación salen los artículos.");
        RuleFor(x => x.ToLocationId).NotEmpty().WithMessage("Elige a qué ubicación van los artículos.");
        RuleFor(x => x.ToLocationId).NotEqual(x => x.FromLocationId).When(x => x.FromLocationId != Guid.Empty)
            .WithMessage(InventoryTransfer.SameLocationMessage);
        RuleFor(x => x.Notes).MaximumLength(InventoryTransfer.MaxNotesLength);
        RuleFor(x => x.Lines).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Agrega al menos un artículo.")
            .Must(lines => lines.Count <= InventoryTransfer.MaxLines).WithMessage(InventoryTransfer.TooManyLinesMessage)
            .Must(lines => lines.All(l => l is not null)).WithMessage("Falta un artículo en la lista.")
            .Must(lines => lines.Select(l => l.SupplyItemId).Distinct().Count() == lines.Count)
            .WithMessage("Un artículo va una sola vez.");
        RuleForEach(x => x.Lines).Where(l => l is not null).ChildRules(line =>
        {
            line.RuleFor(l => l.SupplyItemId).NotEmpty();
            line.RuleFor(l => l.Quantity)
                .Must(q => q > 0 && q <= CreateInventoryTransferCommand.MaxQuantity && decimal.Round(q, UnitTypeRules.QuantityDecimals) == q)
                .WithMessage("La cantidad va de más de 0 a 9,999,999, con máximo 3 decimales.");
        });
    }
}

public class CreateInventoryTransferCommandHandler : IRequestHandler<CreateInventoryTransferCommand, Result<InventoryTransferDto>>
{
    public const string SoldMeanwhileMessage =
        "Se vendió o se movió un artículo mientras se transfería: revisa lo que hay en el origen y vuelve a intentarlo.";
    public const string KeyUsedMessage =
        "Esa transferencia ya se hizo con lo primero que mandaste: revísala en la lista y, si falta algo, haz otra.";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IFolioGenerator _folios;
    private readonly IInventoryLocationLock _locationLock;
    private readonly InventoryLedger _ledger;

    public CreateInventoryTransferCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenant,
        ICurrentUserService currentUser,
        IDateTimeProvider clock,
        IFolioGenerator folios,
        IInventoryLocationLock locationLock,
        InventoryLedger ledger)
    {
        _context = context;
        _tenant = tenant;
        _currentUser = currentUser;
        _clock = clock;
        _folios = folios;
        _locationLock = locationLock;
        _ledger = ledger;
    }

    public async Task<Result<InventoryTransferDto>> Handle(CreateInventoryTransferCommand request, CancellationToken cancellationToken)
    {
        // The same key again (a retry, a double tap): the transfer is already made.
        if (await MadeAsync(request.IdempotencyKey, cancellationToken) is { } made)
            return Replay(request, made);

        // Both locations, in order of their id: two transfers between the same two take turns and never deadlock. Read
        // again once in line: the request this one waited for may be the same transfer.
        foreach (var locationId in new[] { request.FromLocationId, request.ToLocationId }.Order())
            await _locationLock.AcquireAsync(_tenant.TenantId, locationId, cancellationToken);
        if (await MadeAsync(request.IdempotencyKey, cancellationToken) is { } waited)
            return Replay(request, waited);

        var locations = await _context.InventoryLocations.AsNoTracking()
            .Where(l => l.Id == request.FromLocationId || l.Id == request.ToLocationId)
            .ToDictionaryAsync(l => l.Id, cancellationToken);
        if (!locations.TryGetValue(request.FromLocationId, out var origin))
            return Result.Failure<InventoryTransferDto>("La ubicación de origen no existe.");
        if (!locations.TryGetValue(request.ToLocationId, out var destination))
            return Result.Failure<InventoryTransferDto>("La ubicación de destino no existe.");
        var inactive = new[] { origin, destination }.Where(l => !l.IsActive).Select(l => l.Name).ToList();
        if (inactive.Count > 0)
            return Result.Failure<InventoryTransferDto>(
                inactive.Count == 1
                    ? $"{inactive[0]} está inactiva: actívala para transferir."
                    : $"{string.Join(" y ", inactive)} están inactivas: actívalas para transferir.");

        var supplyIds = request.Lines.Select(l => l.SupplyItemId).ToList();
        var supplies = await _context.SupplyItems.AsNoTracking()
            .Where(s => supplyIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, cancellationToken);
        if (supplies.Count != supplyIds.Count)
            return Result.Failure<InventoryTransferDto>("Uno o más artículos no existen.");
        var inactiveSupplies = request.Lines.Select(l => supplies[l.SupplyItemId]).Where(s => !s.IsActive).Select(s => s.Name).ToList();
        if (inactiveSupplies.Count > 0)
            return Result.Failure<InventoryTransferDto>(
                $"Estos artículos están inactivos: {string.Join(", ", inactiveSupplies)}. Actívalos para transferirlos.");
        var fractions = request.Lines
            .Where(l => !UnitTypeRules.AllowsFractions(supplies[l.SupplyItemId].Unit) && decimal.Truncate(l.Quantity) != l.Quantity)
            .Select(l => supplies[l.SupplyItemId].Name)
            .ToList();
        if (fractions.Count > 0)
            return Result.Failure<InventoryTransferDto>($"Estos artículos van en piezas enteras: {string.Join(", ", fractions)}.");

        // What the origin has, read inside the lock. A sale may still take some before saving: the ledger refuses to leave
        // the stock negative and the stock's concurrency token refuses a sale that saved in between.
        var onHand = await _context.InventoryStocks.AsNoTracking()
            .Where(s => s.InventoryLocationId == origin.Id && supplyIds.Contains(s.SupplyItemId))
            .ToDictionaryAsync(s => s.SupplyItemId, s => s.OnHandQuantity, cancellationToken);
        var missing = request.Lines
            .Where(l => onHand.GetValueOrDefault(l.SupplyItemId) < l.Quantity)
            .Select(l => supplies[l.SupplyItemId])
            .OrderBy(s => s.Name)
            .Select(s => $"{s.Name} (hay {UnitTypeRules.FormatQuantity(onHand.GetValueOrDefault(s.Id), s.Unit)})")
            .ToList();
        if (missing.Count > 0)
            return Result.Failure<InventoryTransferDto>($"No hay suficiente en {origin.Name}: {string.Join(", ", missing)}.");

        var now = _clock.UtcNow;
        var transferId = Guid.NewGuid();
        var transferNumber = await _folios.NextInventoryTransferNumberAsync(_tenant.TenantId, now, cancellationToken);
        var costs = supplies.Values.ToDictionary(
            s => s.Id, s => InventoryTransferPostings.UnitCost(s.AverageCost, s.LastCost, s.DefaultCost));

        Result<InventoryMovement> exit;
        try
        {
            exit = await _ledger.PostAsync(_tenant.TenantId, Posting(
                InventoryMovementType.TransferOut, InventoryTransferPostings.OutReference(transferId), transferNumber, now,
                $"Transferencia {transferNumber} a {destination.Name}", request.Lines, origin.Id, costs), cancellationToken);
        }
        catch (DomainException)
        {
            // A sale took the stock between the check above and the ledger: the failed result rolls everything back.
            return Result.Failure<InventoryTransferDto>(SoldMeanwhileMessage);
        }
        if (exit.IsFailure)
            return Result.Failure<InventoryTransferDto>(exit.Error!);

        var entry = await _ledger.PostAsync(_tenant.TenantId, Posting(
            InventoryMovementType.TransferIn, InventoryTransferPostings.InReference(transferId), transferNumber, now,
            $"Transferencia {transferNumber} de {origin.Name}", request.Lines, destination.Id, costs), cancellationToken);
        if (entry.IsFailure)
            return Result.Failure<InventoryTransferDto>(entry.Error!);

        var user = _currentUser.UserName ?? "system";
        var transfer = new InventoryTransfer(
            transferId, _tenant.TenantId, transferNumber, request.IdempotencyKey, origin.Id, destination.Id, now, user,
            exit.Value.Id, entry.Value.Id, request.Lines.Count, decimal.Round(exit.Value.TotalAmount, 4), request.Notes);
        _context.InventoryTransfers.Add(transfer);
        _context.AuditLogs.Add(new AuditLog(
            tenantId: _tenant.TenantId,
            entityName: nameof(InventoryTransfer),
            entityId: transfer.Id.ToString(),
            action: "InventoryTransferCreated",
            performedBy: user,
            metadata: JsonSerializer.Serialize(new
            {
                transfer.TransferNumber,
                transfer.FromLocationId,
                transfer.ToLocationId,
                Items = transfer.LineCount,
                transfer.TotalCost,
                OutMovement = exit.Value.MovementNumber,
                InMovement = entry.Value.MovementNumber
            }),
            timestamp: now));
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception is not DbUpdateConcurrencyException)
        {
            // The same key arrived at once for two other locations and that request stored it first
            // (IX_InventoryTransfers_TenantId_IdempotencyKey): this copy is undone whole and the stored one is the answer.
            await _context.DiscardUnitOfWorkAsync(cancellationToken);
            if (await MadeAsync(request.IdempotencyKey, cancellationToken) is { } stored)
                return Replay(request, stored);
            throw;
        }

        return Result.Success((await InventoryTransferReads.TransferAsync(_context, transfer.Id, cancellationToken))!);
    }

    /// <summary>
    /// The transfer the key already made, when it is the one asked for. With other locations or items it is not: the app
    /// lost the answer and the person changed the transfer, so they are told it was made with what was sent first.
    /// </summary>
    private static Result<InventoryTransferDto> Replay(CreateInventoryTransferCommand request, InventoryTransferDto made)
    {
        var asked = request.Lines.Select(l => (l.SupplyItemId, l.Quantity)).Order();
        var moved = made.Lines.Select(l => (l.SupplyItemId, l.Quantity)).Order();
        return made.FromLocationId == request.FromLocationId && made.ToLocationId == request.ToLocationId && asked.SequenceEqual(moved)
            ? Result.Success(made)
            : Result.Failure<InventoryTransferDto>(KeyUsedMessage);
    }

    private async Task<InventoryTransferDto?> MadeAsync(Guid idempotencyKey, CancellationToken cancellationToken)
    {
        var id = await _context.InventoryTransfers.AsNoTracking()
            .Where(t => t.IdempotencyKey == idempotencyKey)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return id is { } transferId ? await InventoryTransferReads.TransferAsync(_context, transferId, cancellationToken) : null;
    }

    private static InventoryPosting Posting(
        InventoryMovementType type,
        string reference,
        string transferNumber,
        DateTimeOffset now,
        string notes,
        IEnumerable<InventoryTransferLineRequest> lines,
        Guid locationId,
        IReadOnlyDictionary<Guid, decimal?> costs)
        => new(type, InventoryMovementReason.Transfer, now,
            lines.Select(l => new InventoryPostingLine(l.SupplyItemId, locationId, l.Quantity) { UnitCost = costs[l.SupplyItemId] }).ToList())
        {
            ReferenceNumber = transferNumber,
            ExternalReference = reference,
            Notes = notes
        };
}
