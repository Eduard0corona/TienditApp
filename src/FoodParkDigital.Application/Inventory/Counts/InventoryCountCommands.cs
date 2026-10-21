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

namespace FoodParkDigital.Application.Inventory.Counts;

/// <summary>
/// The adjustments a physical count posts (Fase 27A, ADR 0037): one that adds what was found over the system and one that
/// takes what was missing, each with an external reference of the count. The unique index of the references keeps a count
/// from moving the stock twice, and manual movements cannot use the prefix.
/// </summary>
public static class InventoryCountPostings
{
    public const string ReferencePrefix = "COUNT:";

    public static string IncreaseReference(Guid countId) => $"{ReferencePrefix}{countId:N}:IN";

    public static string DecreaseReference(Guid countId) => $"{ReferencePrefix}{countId:N}:OUT";
}

/// <summary>An open count of the business, taken inside the location's lock.</summary>
internal static class OpenInventoryCount
{
    public static async Task<Result<InventoryCount>> TakeAsync(
        IApplicationDbContext context, IInventoryLocationLock locationLock, Guid tenantId, Guid countId, CancellationToken cancellationToken)
    {
        var locationId = await context.InventoryCounts.AsNoTracking()
                .Where(c => c.Id == countId)
                .Select(c => (Guid?)c.InventoryLocationId)
                .FirstOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException(nameof(InventoryCount), countId);

        // Read again once in line: whatever was waiting for the lock may have posted or cancelled it.
        await locationLock.AcquireAsync(tenantId, locationId, cancellationToken);
        var count = await context.InventoryCounts.FirstAsync(c => c.Id == countId, cancellationToken);
        return count.IsOpen ? Result.Success(count) : Result.Failure<InventoryCount>(InventoryCount.NotOpenMessage);
    }
}

/// <summary>
/// Empezar un conteo (Fase 27A, ADR 0037): the items to count in a location, those with stock there (all or one category)
/// or the chosen ones. One open count per location.
/// </summary>
[RequiresPermission(TenantPermission.ManageInventory)]
[RequiresCapability(TenantCapability.Inventory)]
public record StartInventoryCountCommand : IRequest<Result<InventoryCountDto>>, ITransactionalRequest
{
    public Guid InventoryLocationId { get; init; }
    /// <summary>Only the items of this category with stock in the location.</summary>
    public string? CategoryName { get; init; }
    /// <summary>The items to count, instead of the ones with stock in the location.</summary>
    public List<Guid> SupplyItemIds { get; init; } = [];
    public string? Notes { get; init; }
}

public class StartInventoryCountCommandValidator : AbstractValidator<StartInventoryCountCommand>
{
    public StartInventoryCountCommandValidator()
    {
        RuleFor(x => x.InventoryLocationId).NotEmpty().WithMessage("Elige la ubicación que vas a contar.");
        RuleFor(x => x.CategoryName).MaximumLength(InventoryCount.MaxCategoryNameLength);
        RuleFor(x => x.SupplyItemIds).Cascade(CascadeMode.Stop).NotNull()
            .Must(ids => ids.Count <= InventoryCount.MaxLines).WithMessage(InventoryCount.TooManyLinesMessage);
        RuleForEach(x => x.SupplyItemIds).NotEmpty();
        RuleFor(x => x).Must(x => x.SupplyItemIds is not { Count: > 0 } || string.IsNullOrWhiteSpace(x.CategoryName))
            .WithMessage("Cuenta una categoría o los artículos que elijas, no las dos cosas.");
        RuleFor(x => x.Notes).MaximumLength(InventoryCount.MaxNotesLength);
    }
}

public class StartInventoryCountCommandHandler : IRequestHandler<StartInventoryCountCommand, Result<InventoryCountDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IFolioGenerator _folios;
    private readonly IInventoryLocationLock _locationLock;

    public StartInventoryCountCommandHandler(
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

    public async Task<Result<InventoryCountDto>> Handle(StartInventoryCountCommand request, CancellationToken cancellationToken)
    {
        await _locationLock.AcquireAsync(_tenant.TenantId, request.InventoryLocationId, cancellationToken);

        var location = await _context.InventoryLocations.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == request.InventoryLocationId, cancellationToken);
        if (location is null)
            return Result.Failure<InventoryCountDto>("La ubicación no existe.");
        if (!location.IsActive)
            return Result.Failure<InventoryCountDto>("La ubicación está inactiva: actívala para contarla.");

        var open = await _context.InventoryCounts.AsNoTracking()
            .Where(c => c.InventoryLocationId == location.Id && c.Status == InventoryCountStatus.InProgress)
            .Select(c => c.CountNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (open is not null)
            return Result.Failure<InventoryCountDto>(
                $"Ya hay un conteo abierto de {location.Name} ({open}): termínalo o cancélalo antes de empezar otro.");

        var category = string.IsNullOrWhiteSpace(request.CategoryName) ? null : request.CategoryName.Trim();
        List<Guid> supplyIds;
        if (request.SupplyItemIds.Count > 0)
        {
            var chosen = request.SupplyItemIds.Distinct().ToList();
            supplyIds = await _context.SupplyItems.AsNoTracking()
                .Where(s => chosen.Contains(s.Id) && s.IsActive)
                .Select(s => s.Id)
                .ToListAsync(cancellationToken);
            if (supplyIds.Count != chosen.Count)
                return Result.Failure<InventoryCountDto>("Uno o más artículos no existen o están inactivos.");
        }
        else
        {
            var inLocation =
                from stock in _context.InventoryStocks.AsNoTracking()
                join supply in _context.SupplyItems.AsNoTracking() on stock.SupplyItemId equals supply.Id
                where stock.InventoryLocationId == location.Id && supply.IsActive
                select supply;
            if (category is not null)
            {
                var lowered = category.ToLower();
                inLocation = inLocation.Where(s => s.CategoryName != null && s.CategoryName.Trim().ToLower() == lowered);
            }

            supplyIds = await inLocation.Select(s => s.Id).Distinct().Take(InventoryCount.MaxLines + 1).ToListAsync(cancellationToken);
            if (supplyIds.Count == 0)
                return Result.Failure<InventoryCountDto>(category is null
                    ? $"No hay artículos con existencias en {location.Name}: elige los artículos que vas a contar."
                    : $"No hay artículos de «{category}» con existencias en {location.Name}: elige los artículos que vas a contar.");
        }
        if (supplyIds.Count > InventoryCount.MaxLines)
            return Result.Failure<InventoryCountDto>(InventoryCount.TooManyLinesMessage);

        var now = _clock.UtcNow;
        var count = new InventoryCount(
            _tenant.TenantId,
            await _folios.NextInventoryCountNumberAsync(_tenant.TenantId, now, cancellationToken),
            location.Id,
            now,
            _currentUser.UserName ?? "system",
            request.SupplyItemIds.Count > 0 ? null : category,
            request.Notes);
        _context.InventoryCounts.Add(count);
        foreach (var supplyId in supplyIds)
            _context.InventoryCountLines.Add(count.AddLine(supplyId));
        _context.AuditLogs.Add(new AuditLog(
            tenantId: _tenant.TenantId,
            entityName: nameof(InventoryCount),
            entityId: count.Id.ToString(),
            action: "InventoryCountStarted",
            performedBy: count.StartedBy,
            metadata: JsonSerializer.Serialize(new
            {
                count.CountNumber,
                count.InventoryLocationId,
                count.CategoryName,
                Chosen = request.SupplyItemIds.Count > 0,
                Items = count.LineCount
            }),
            timestamp: now));
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success((await InventoryCountReads.CountAsync(_context, count.Id, cancellationToken))!);
    }
}

/// <summary>What was counted of one item; no quantity leaves it uncounted.</summary>
public record InventoryCountEntry
{
    public Guid LineId { get; init; }
    public decimal? CountedQuantity { get; init; }
    public string? Notes { get; init; }
}

/// <summary>
/// Capturar lo contado (Fase 27A, ADR 0037): each quantity is saved with what the system had of the item in the location at
/// that moment, which the difference is measured against.
/// </summary>
[RequiresPermission(TenantPermission.ManageInventory)]
[RequiresCapability(TenantCapability.Inventory)]
public record RecordInventoryCountCommand : IRequest<Result<RecordedInventoryCountDto>>, ITransactionalRequest
{
    public const int MaxEntries = 200;

    public Guid CountId { get; init; }
    public List<InventoryCountEntry> Lines { get; init; } = [];
}

public class RecordInventoryCountCommandValidator : AbstractValidator<RecordInventoryCountCommand>
{
    public RecordInventoryCountCommandValidator()
    {
        RuleFor(x => x.CountId).NotEmpty();
        RuleFor(x => x.Lines).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Escribe lo que contaste de al menos un artículo.")
            .Must(lines => lines.Count <= RecordInventoryCountCommand.MaxEntries)
            .WithMessage($"Guarda como máximo {RecordInventoryCountCommand.MaxEntries} artículos a la vez.")
            .Must(lines => lines.All(l => l is not null)).WithMessage("Falta un artículo en la lista.")
            .Must(lines => lines.Select(l => l.LineId).Distinct().Count() == lines.Count)
            .WithMessage("Un artículo va una sola vez.");
        RuleForEach(x => x.Lines).Where(l => l is not null).ChildRules(line =>
        {
            line.RuleFor(l => l.LineId).NotEmpty();
            line.RuleFor(l => l.CountedQuantity)
                .Must(quantity => quantity is not { } q
                    || (q >= 0 && q <= InventoryCountLine.MaxQuantity && decimal.Round(q, UnitTypeRules.QuantityDecimals) == q))
                .WithMessage("La cantidad contada va de 0 a 9,999,999, con máximo 3 decimales.");
            line.RuleFor(l => l.Notes).MaximumLength(InventoryCountLine.MaxNotesLength);
        });
    }
}

public class RecordInventoryCountCommandHandler : IRequestHandler<RecordInventoryCountCommand, Result<RecordedInventoryCountDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IInventoryLocationLock _locationLock;

    public RecordInventoryCountCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenant,
        ICurrentUserService currentUser,
        IDateTimeProvider clock,
        IInventoryLocationLock locationLock)
    {
        _context = context;
        _tenant = tenant;
        _currentUser = currentUser;
        _clock = clock;
        _locationLock = locationLock;
    }

    public async Task<Result<RecordedInventoryCountDto>> Handle(RecordInventoryCountCommand request, CancellationToken cancellationToken)
    {
        var taken = await OpenInventoryCount.TakeAsync(_context, _locationLock, _tenant.TenantId, request.CountId, cancellationToken);
        if (taken.IsFailure)
            return Result.Failure<RecordedInventoryCountDto>(taken.Error!);
        var count = taken.Value;

        var lineIds = request.Lines.Select(l => l.LineId).ToList();
        var lines = await _context.InventoryCountLines
            .Where(l => l.CountId == count.Id && lineIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, cancellationToken);
        if (lines.Count != lineIds.Count)
            return Result.Failure<RecordedInventoryCountDto>("Uno o más artículos no son de este conteo.");

        var supplyIds = lines.Values.Select(l => l.SupplyItemId).ToList();
        var supplies = await _context.SupplyItems.AsNoTracking()
            .Where(s => supplyIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name, s.Unit, s.IsActive })
            .ToDictionaryAsync(s => s.Id, cancellationToken);
        var onHand = await _context.InventoryStocks.AsNoTracking()
            .Where(s => s.InventoryLocationId == count.InventoryLocationId && supplyIds.Contains(s.SupplyItemId))
            .ToDictionaryAsync(s => s.SupplyItemId, s => s.OnHandQuantity, cancellationToken);

        var now = _clock.UtcNow;
        var user = _currentUser.UserName ?? "system";
        var corrections = new List<object>();
        foreach (var entry in request.Lines)
        {
            var line = lines[entry.LineId];
            var supply = supplies[line.SupplyItemId];
            if (entry.CountedQuantity is { } quantity)
            {
                if (!supply.IsActive && quantity != line.CountedQuantity)
                    return Result.Failure<RecordedInventoryCountDto>($"{supply.Name} está inactivo: actívalo para contarlo.");
                if (!InventoryCount.IsValidCountedQuantity(quantity, supply.Unit))
                    return Result.Failure<RecordedInventoryCountDto>($"{supply.Name}: {Lowercase(InventoryCount.CountedQuantityMessage(supply.Unit))}");
            }

            // Changing or clearing what someone counted is how shrinkage hides: who did it, and from what, is kept.
            if (line.CountedQuantity is { } before && entry.CountedQuantity != before)
                corrections.Add(new { line.SupplyItemId, supply.Name, From = before, To = entry.CountedQuantity, line.SystemQuantity, line.CountedBy });

            count.Record(line, entry.CountedQuantity, onHand.GetValueOrDefault(line.SupplyItemId), supply.Unit, now, user, entry.Notes);
        }
        if (corrections.Count > 0)
            _context.AuditLogs.Add(new AuditLog(
                tenantId: _tenant.TenantId,
                entityName: nameof(InventoryCount),
                entityId: count.Id.ToString(),
                action: "InventoryCountCorrected",
                performedBy: user,
                metadata: JsonSerializer.Serialize(new { count.CountNumber, Changes = corrections }),
                timestamp: now));
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new RecordedInventoryCountDto
        {
            Count = (await InventoryCountReads.CountAsync(_context, count.Id, cancellationToken))!,
            Lines = await InventoryCountReads.LinesAsync(_context, count, lineIds, cancellationToken)
        });
    }

    private static string Lowercase(string message) => char.ToLowerInvariant(message[0]) + message[1..];
}

/// <summary>
/// Agregar artículos a un conteo abierto (Fase 27A, ADR 0037): what turned up on the shelf, even without stock in the
/// location. An item the count already has stays as it is.
/// </summary>
[RequiresPermission(TenantPermission.ManageInventory)]
[RequiresCapability(TenantCapability.Inventory)]
public record AddInventoryCountItemsCommand : IRequest<Result<InventoryCountDto>>, ITransactionalRequest
{
    public const int MaxItems = 200;

    public Guid CountId { get; init; }
    public List<Guid> SupplyItemIds { get; init; } = [];
}

public class AddInventoryCountItemsCommandValidator : AbstractValidator<AddInventoryCountItemsCommand>
{
    public AddInventoryCountItemsCommandValidator()
    {
        RuleFor(x => x.CountId).NotEmpty();
        RuleFor(x => x.SupplyItemIds).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Elige los artículos que vas a agregar.")
            .Must(ids => ids.Count <= AddInventoryCountItemsCommand.MaxItems)
            .WithMessage($"Agrega como máximo {AddInventoryCountItemsCommand.MaxItems} artículos a la vez.");
        RuleForEach(x => x.SupplyItemIds).NotEmpty();
    }
}

public class AddInventoryCountItemsCommandHandler : IRequestHandler<AddInventoryCountItemsCommand, Result<InventoryCountDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IInventoryLocationLock _locationLock;

    public AddInventoryCountItemsCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenant,
        ICurrentUserService currentUser,
        IDateTimeProvider clock,
        IInventoryLocationLock locationLock)
    {
        _context = context;
        _tenant = tenant;
        _currentUser = currentUser;
        _clock = clock;
        _locationLock = locationLock;
    }

    public async Task<Result<InventoryCountDto>> Handle(AddInventoryCountItemsCommand request, CancellationToken cancellationToken)
    {
        var taken = await OpenInventoryCount.TakeAsync(_context, _locationLock, _tenant.TenantId, request.CountId, cancellationToken);
        if (taken.IsFailure)
            return Result.Failure<InventoryCountDto>(taken.Error!);
        var count = taken.Value;

        var chosen = request.SupplyItemIds.Distinct().ToList();
        var already = await _context.InventoryCountLines.AsNoTracking()
            .Where(l => l.CountId == count.Id && chosen.Contains(l.SupplyItemId))
            .Select(l => l.SupplyItemId)
            .ToListAsync(cancellationToken);
        var added = chosen.Except(already).ToList();
        if (added.Count > 0)
        {
            var active = await _context.SupplyItems.AsNoTracking()
                .CountAsync(s => added.Contains(s.Id) && s.IsActive, cancellationToken);
            if (active != added.Count)
                return Result.Failure<InventoryCountDto>("Uno o más artículos no existen o están inactivos.");
            if (count.LineCount + added.Count > InventoryCount.MaxLines)
                return Result.Failure<InventoryCountDto>(InventoryCount.TooManyLinesMessage);

            foreach (var supplyId in added)
                _context.InventoryCountLines.Add(count.AddLine(supplyId));
            _context.AuditLogs.Add(new AuditLog(
                tenantId: _tenant.TenantId,
                entityName: nameof(InventoryCount),
                entityId: count.Id.ToString(),
                action: "InventoryCountItemsAdded",
                performedBy: _currentUser.UserName ?? "system",
                metadata: JsonSerializer.Serialize(new { count.CountNumber, Added = added }),
                timestamp: _clock.UtcNow));
            await _context.SaveChangesAsync(cancellationToken);
        }

        return Result.Success((await InventoryCountReads.CountAsync(_context, count.Id, cancellationToken))!);
    }
}

/// <summary>
/// Aplicar un conteo (Fase 27A, ADR 0037): each counted item moves the stock the location has now by its difference, with an
/// adjustment of reason <see cref="InventoryMovementReason.InventoryCount"/> valued at its average cost. The items not
/// counted are left as they are. The count must be the one that was reviewed, and no item may end below zero.
/// </summary>
[RequiresPermission(TenantPermission.ManageInventory)]
[RequiresCapability(TenantCapability.Inventory)]
public record PostInventoryCountCommand : IRequest<Result<InventoryCountDto>>, ITransactionalRequest
{
    public Guid CountId { get; init; }
    /// <summary>The revision of the count that was reviewed.</summary>
    public int ExpectedRevision { get; init; }
}

public class PostInventoryCountCommandValidator : AbstractValidator<PostInventoryCountCommand>
{
    public PostInventoryCountCommandValidator()
    {
        RuleFor(x => x.CountId).NotEmpty();
        RuleFor(x => x.ExpectedRevision).GreaterThanOrEqualTo(0);
    }
}

public class PostInventoryCountCommandHandler : IRequestHandler<PostInventoryCountCommand, Result<InventoryCountDto>>
{
    public const string ChangedMessage = "El conteo cambió mientras lo revisabas: revísalo otra vez antes de aplicarlo.";
    public const string SoldMeanwhileMessage = "Se vendió o se movió un artículo mientras se aplicaba el conteo: revísalo otra vez y vuelve a aplicarlo.";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IInventoryLocationLock _locationLock;
    private readonly InventoryLedger _ledger;

    public PostInventoryCountCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenant,
        ICurrentUserService currentUser,
        IDateTimeProvider clock,
        IInventoryLocationLock locationLock,
        InventoryLedger ledger)
    {
        _context = context;
        _tenant = tenant;
        _currentUser = currentUser;
        _clock = clock;
        _locationLock = locationLock;
        _ledger = ledger;
    }

    public async Task<Result<InventoryCountDto>> Handle(PostInventoryCountCommand request, CancellationToken cancellationToken)
    {
        var taken = await OpenInventoryCount.TakeAsync(_context, _locationLock, _tenant.TenantId, request.CountId, cancellationToken);
        if (taken.IsFailure)
            return Result.Failure<InventoryCountDto>(taken.Error!);
        var count = taken.Value;
        if (count.Revision != request.ExpectedRevision)
            return Result.Failure<InventoryCountDto>(ChangedMessage);
        if (count.CountedLineCount == 0)
            return Result.Failure<InventoryCountDto>(InventoryCount.NothingCountedMessage);

        var differences = await (
                from line in _context.InventoryCountLines.AsNoTracking()
                join supply in _context.SupplyItems.AsNoTracking() on line.SupplyItemId equals supply.Id
                where line.CountId == count.Id && line.CountedQuantity != null && line.CountedQuantity != line.SystemQuantity
                orderby supply.Name
                select new { Line = line, supply.Name, supply.Unit, supply.IsActive })
            .ToListAsync(cancellationToken);

        if (differences.Count > 0)
        {
            var locationActive = await _context.InventoryLocations.AsNoTracking()
                .AnyAsync(l => l.Id == count.InventoryLocationId && l.IsActive, cancellationToken);
            if (!locationActive)
                return Result.Failure<InventoryCountDto>("La ubicación está inactiva: actívala para aplicar el conteo.");

            var inactive = differences.Where(d => !d.IsActive).Select(d => d.Name).ToList();
            if (inactive.Count > 0)
                return Result.Failure<InventoryCountDto>(
                    $"Estos artículos están inactivos: {string.Join(", ", inactive)}. Actívalos o bórrales la cantidad para aplicar el conteo.");

            var supplyIds = differences.Select(d => d.Line.SupplyItemId).ToList();
            var onHand = await _context.InventoryStocks.AsNoTracking()
                .Where(s => s.InventoryLocationId == count.InventoryLocationId && supplyIds.Contains(s.SupplyItemId))
                .ToDictionaryAsync(s => s.SupplyItemId, s => s.OnHandQuantity, cancellationToken);
            var negative = differences
                .Select(d => (d.Name, d.Unit, Resulting: d.Line.ResultingQuantity(onHand.GetValueOrDefault(d.Line.SupplyItemId))!.Value))
                .Where(d => d.Resulting < 0)
                .Select(d => $"{d.Name} ({UnitTypeRules.FormatQuantity(d.Resulting, d.Unit)})")
                .ToList();
            if (negative.Count > 0)
                return Result.Failure<InventoryCountDto>(
                    $"Quedarían en negativo porque se movieron después de contarlos: {string.Join(", ", negative)}. Vuélvelos a contar.");
        }

        var now = _clock.UtcNow;
        InventoryMovement? increase = null;
        InventoryMovement? decrease = null;
        var surplus = differences.Where(d => d.Line.Difference > 0).ToList();
        if (surplus.Count > 0)
        {
            var posted = await _ledger.PostAsync(_tenant.TenantId, Posting(
                count, InventoryMovementType.AdjustmentIncrease, InventoryCountPostings.IncreaseReference(count.Id), now,
                surplus.Select(d => Line(count, d.Line, d.Unit)).ToList()), cancellationToken);
            if (posted.IsFailure)
                return Result.Failure<InventoryCountDto>(posted.Error!);
            increase = posted.Value;
        }

        var shortage = differences.Where(d => d.Line.Difference < 0).ToList();
        if (shortage.Count > 0)
        {
            Result<InventoryMovement> posted;
            try
            {
                posted = await _ledger.PostAsync(_tenant.TenantId, Posting(
                    count, InventoryMovementType.AdjustmentDecrease, InventoryCountPostings.DecreaseReference(count.Id), now,
                    shortage.Select(d => Line(count, d.Line, d.Unit)).ToList()), cancellationToken);
            }
            catch (DomainException)
            {
                // A sale took the stock between the check above and the ledger: the failed result rolls everything back.
                return Result.Failure<InventoryCountDto>(SoldMeanwhileMessage);
            }
            if (posted.IsFailure)
                return Result.Failure<InventoryCountDto>(posted.Error!);
            decrease = posted.Value;
        }

        var user = _currentUser.UserName ?? "system";
        count.Post(now, user, increase?.Id, decrease?.Id);
        _context.AuditLogs.Add(new AuditLog(
            tenantId: _tenant.TenantId,
            entityName: nameof(InventoryCount),
            entityId: count.Id.ToString(),
            action: "InventoryCountPosted",
            performedBy: user,
            metadata: JsonSerializer.Serialize(new
            {
                count.CountNumber,
                count.InventoryLocationId,
                Counted = count.CountedLineCount,
                Differences = differences.Count,
                SurplusValue = increase?.TotalAmount ?? 0m,
                ShortageValue = decrease?.TotalAmount ?? 0m,
                IncreaseMovement = increase?.MovementNumber,
                DecreaseMovement = decrease?.MovementNumber
            }),
            timestamp: now));
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success((await InventoryCountReads.CountAsync(_context, count.Id, cancellationToken))!);
    }

    private static InventoryPosting Posting(
        InventoryCount count, InventoryMovementType type, string reference, DateTimeOffset now, IReadOnlyList<InventoryPostingLine> lines)
        => new(type, InventoryMovementReason.InventoryCount, now, lines)
        {
            ReferenceNumber = count.CountNumber,
            ExternalReference = reference,
            Notes = $"Conteo físico {count.CountNumber}"
        };

    private static InventoryPostingLine Line(InventoryCount count, InventoryCountLine line, Domain.Catalog.Enums.UnitType unit)
        => new(line.SupplyItemId, count.InventoryLocationId, Math.Abs(line.Difference!.Value))
        {
            Notes = $"Contado {UnitTypeRules.FormatQuantity(line.CountedQuantity!.Value, unit)}; "
                + $"sistema al contar {UnitTypeRules.FormatQuantity(line.SystemQuantity!.Value, unit)}"
        };
}

/// <summary>Cancelar un conteo abierto (Fase 27A, ADR 0037): nothing moves, and the count stays with what was counted.</summary>
[RequiresPermission(TenantPermission.ManageInventory)]
[RequiresCapability(TenantCapability.Inventory)]
public record CancelInventoryCountCommand : IRequest<Result<InventoryCountDto>>, ITransactionalRequest
{
    public Guid CountId { get; init; }
    public string? Reason { get; init; }
}

public class CancelInventoryCountCommandValidator : AbstractValidator<CancelInventoryCountCommand>
{
    public CancelInventoryCountCommandValidator()
    {
        RuleFor(x => x.CountId).NotEmpty();
        RuleFor(x => x.Reason).MaximumLength(InventoryCount.MaxCancelReasonLength);
    }
}

public class CancelInventoryCountCommandHandler : IRequestHandler<CancelInventoryCountCommand, Result<InventoryCountDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IInventoryLocationLock _locationLock;

    public CancelInventoryCountCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenant,
        ICurrentUserService currentUser,
        IDateTimeProvider clock,
        IInventoryLocationLock locationLock)
    {
        _context = context;
        _tenant = tenant;
        _currentUser = currentUser;
        _clock = clock;
        _locationLock = locationLock;
    }

    public async Task<Result<InventoryCountDto>> Handle(CancelInventoryCountCommand request, CancellationToken cancellationToken)
    {
        var taken = await OpenInventoryCount.TakeAsync(_context, _locationLock, _tenant.TenantId, request.CountId, cancellationToken);
        if (taken.IsFailure)
            return Result.Failure<InventoryCountDto>(taken.Error!);
        var count = taken.Value;

        var now = _clock.UtcNow;
        var user = _currentUser.UserName ?? "system";
        count.Cancel(now, user, request.Reason);
        _context.AuditLogs.Add(new AuditLog(
            tenantId: _tenant.TenantId,
            entityName: nameof(InventoryCount),
            entityId: count.Id.ToString(),
            action: "InventoryCountCancelled",
            performedBy: user,
            metadata: JsonSerializer.Serialize(new { count.CountNumber, count.InventoryLocationId, Counted = count.CountedLineCount, count.CancelReason }),
            timestamp: now));
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success((await InventoryCountReads.CountAsync(_context, count.Id, cancellationToken))!);
    }
}
