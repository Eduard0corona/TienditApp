using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Delivery;

/// <summary>
/// Adds a delivery zone (owners and admins): its name, the postal codes it reaches, its delivery fee, the smallest order
/// it takes and, optionally, from what order the delivery is free. A name or a postal code already in another zone of the
/// business is refused, naming that zone.
/// </summary>
[RequiresTenantManager]
public record CreateDeliveryZoneCommand(
    string Name,
    decimal Fee,
    decimal MinimumOrder,
    decimal? FreeDeliveryFrom,
    IReadOnlyList<string> PostalCodes,
    bool IsActive = true)
    : IRequest<Result<DeliveryZoneDto>>, IDeliveryZoneFields;

/// <summary>
/// Saves a delivery zone with the revision the person saw (owners and admins). Turning it off keeps its postal codes; a
/// save in between from someone else is refused, and the person reloads.
/// </summary>
[RequiresTenantManager]
public record UpdateDeliveryZoneCommand(
    Guid Id,
    int Revision,
    string Name,
    decimal Fee,
    decimal MinimumOrder,
    decimal? FreeDeliveryFrom,
    IReadOnlyList<string> PostalCodes,
    bool IsActive)
    : IRequest<Result<DeliveryZoneDto>>, IDeliveryZoneFields;

/// <summary>
/// Deletes a delivery zone with the revision the person saw (owners and admins): its postal codes are free for another
/// zone. The activity entry keeps its name, fee and how many postal codes it had.
/// </summary>
[RequiresTenantManager]
public record DeleteDeliveryZoneCommand(Guid Id, int Revision) : IRequest<Result>;

/// <summary>What a save of a zone carries, for the one validator of both.</summary>
public interface IDeliveryZoneFields
{
    string Name { get; }
    decimal Fee { get; }
    decimal MinimumOrder { get; }
    decimal? FreeDeliveryFrom { get; }
    IReadOnlyList<string> PostalCodes { get; }
    bool IsActive { get; }
}

public static class DeliveryZoneErrors
{
    /// <summary>
    /// Someone saved or deleted the zone since the person loaded it: the same text as any other change saved at the same
    /// time (409 <c>concurrency_conflict</c>). The client reads it again.
    /// </summary>
    public const string Stale = "Alguien más modificó este registro al mismo tiempo. Recarga e intenta de nuevo.";

    /// <summary>The longest postal code written: something longer is not one, whatever it has around.</summary>
    public const int MaxWrittenLength = 20;

    /// <summary>How many postal codes a save may carry before they are counted once each: twice what a zone holds.</summary>
    public const int MaxWritten = DeliveryZone.MaxPostalCodes * 2;

    public static string PostalCodeInAnotherZone(string postalCode, string zone)
        => $"El código postal {postalCode} ya está en la zona «{zone}». Quítalo de ahí primero.";

    /// <summary>
    /// Another zone of the business has the name (IX_DeliveryZones_TenantId_Name, which compares as the database does:
    /// without telling capitals apart).
    /// </summary>
    public static string NameTaken(string name) => $"Ya tienes una zona llamada «{name}». Ponle otro nombre.";
}

internal static class DeliveryZoneFieldRules
{
    public static void Apply<T>(AbstractValidator<T> validator) where T : IDeliveryZoneFields
    {
        validator.RuleFor(x => x.Name).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(DeliveryZone.NameRequiredMessage)
            .Must(name => name.Trim().Length <= DeliveryZone.MaxNameLength).WithMessage(DeliveryZone.NameTooLongMessage);
        validator.RuleFor(x => x.PostalCodes).Cascade(CascadeMode.Stop)
            .NotNull().WithMessage(DeliveryZone.PostalCodesRequiredMessage)
            .Must(codes => codes.Count <= DeliveryZoneErrors.MaxWritten).WithMessage(DeliveryZone.TooManyPostalCodesMessage)
            .Must(codes => codes.All(code => code is null || code.Length <= DeliveryZoneErrors.MaxWrittenLength))
            .WithMessage("Revisa los códigos postales: cada uno lleva 5 dígitos.");
    }

    public static DeliveryZoneFields ToFields(this IDeliveryZoneFields request)
        => new(request.Name, request.Fee, request.MinimumOrder, request.FreeDeliveryFrom, request.PostalCodes, request.IsActive);
}

public class CreateDeliveryZoneCommandValidator : AbstractValidator<CreateDeliveryZoneCommand>
{
    public CreateDeliveryZoneCommandValidator() => DeliveryZoneFieldRules.Apply(this);
}

public class UpdateDeliveryZoneCommandValidator : AbstractValidator<UpdateDeliveryZoneCommand>
{
    public UpdateDeliveryZoneCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Recarga e intenta de nuevo.");
        RuleFor(x => x.Revision).GreaterThanOrEqualTo(1).WithMessage("Recarga e intenta de nuevo.");
        DeliveryZoneFieldRules.Apply(this);
    }
}

public class DeleteDeliveryZoneCommandValidator : AbstractValidator<DeleteDeliveryZoneCommand>
{
    public DeleteDeliveryZoneCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Recarga e intenta de nuevo.");
        RuleFor(x => x.Revision).GreaterThanOrEqualTo(1).WithMessage("Recarga e intenta de nuevo.");
    }
}

public class CreateDeliveryZoneCommandHandler : IRequestHandler<CreateDeliveryZoneCommand, Result<DeliveryZoneDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUserService;

    public CreateDeliveryZoneCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUserService = currentUserService;
    }

    public async Task<Result<DeliveryZoneDto>> Handle(CreateDeliveryZoneCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.TenantId;
        // A soft cap: two zones added at the same moment can leave a business one or two over it, which is harmless.
        if (await _context.DeliveryZones.CountAsync(zone => zone.TenantId == tenantId, cancellationToken) >= DeliveryZone.MaxZonesPerBusiness)
            return Result.Failure<DeliveryZoneDto>(DeliveryZone.TooManyZonesMessage);

        DeliveryZone zone;
        try
        {
            zone = new DeliveryZone(tenantId, request.ToFields());
        }
        catch (DomainException ex)
        {
            return Result.Failure<DeliveryZoneDto>(ex.Message);
        }

        if (await DeliveryZoneConflicts.FindAsync(_context, tenantId, zone, cancellationToken) is { } conflict)
            return Result.Failure<DeliveryZoneDto>(conflict);

        _context.DeliveryZones.Add(zone);
        _context.AuditLogs.Add(DeliveryZoneAudit.Create(tenantId, zone, DeliveryZoneAudit.Created, null, _currentUserService.UserName));
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another save took its name or one of its postal codes at the same time (the unique indexes): this one is
            // undone whole, its activity entry included, and the person is told which.
            await _context.DiscardUnitOfWorkAsync(cancellationToken);
            if (await DeliveryZoneConflicts.FindAsync(_context, tenantId, zone, cancellationToken) is { } meanwhile)
                return Result.Failure<DeliveryZoneDto>(meanwhile);
            throw;
        }

        return Result.Success(await DeliveryZoneReader.LoadAsync(_context, zone.Id, cancellationToken));
    }
}

public class UpdateDeliveryZoneCommandHandler : IRequestHandler<UpdateDeliveryZoneCommand, Result<DeliveryZoneDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUserService;

    public UpdateDeliveryZoneCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUserService = currentUserService;
    }

    public async Task<Result<DeliveryZoneDto>> Handle(UpdateDeliveryZoneCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.TenantId;
        var zone = await _context.DeliveryZones
            .Include(z => z.PostalCodes)
            .FirstOrDefaultAsync(z => z.Id == request.Id && z.TenantId == tenantId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(DeliveryZone), request.Id);
        if (zone.Revision != request.Revision)
            return Result.Failure<DeliveryZoneDto>(DeliveryZoneErrors.Stale);

        var previous = DeliveryZoneAudit.Describe(zone);
        try
        {
            // Nothing changed: nothing is saved or recorded.
            if (!zone.Update(request.ToFields()))
                return Result.Success(await DeliveryZoneReader.LoadAsync(_context, zone.Id, cancellationToken));
        }
        catch (DomainException ex)
        {
            return Result.Failure<DeliveryZoneDto>(ex.Message);
        }

        if (await DeliveryZoneConflicts.FindAsync(_context, tenantId, zone, cancellationToken) is { } conflict)
        {
            // The zone took the change in memory: it is undone, so nothing of it is saved by a later SaveChanges.
            await _context.DiscardUnitOfWorkAsync(cancellationToken);
            return Result.Failure<DeliveryZoneDto>(conflict);
        }

        _context.AuditLogs.Add(DeliveryZoneAudit.Create(tenantId, zone, DeliveryZoneAudit.Updated, previous, _currentUserService.UserName));
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone saved or deleted the zone after it was read here: this edit was made over an older revision. It is
            // undone whole, its activity entry included.
            await _context.DiscardUnitOfWorkAsync(cancellationToken);
            return Result.Failure<DeliveryZoneDto>(DeliveryZoneErrors.Stale);
        }
        catch (DbUpdateException)
        {
            // Whatever order the database ran the rows in: a new postal code of the zone can be written before the zone
            // itself, and then someone else's save shows as its unique index or as the key to a zone deleted meanwhile,
            // not as a revision that moved. Undone whole, and told apart by reading again.
            await _context.DiscardUnitOfWorkAsync(cancellationToken);
            if (!await DeliveryZoneConflicts.StillAtAsync(_context, tenantId, request.Id, request.Revision, cancellationToken))
                return Result.Failure<DeliveryZoneDto>(DeliveryZoneErrors.Stale);
            if (await DeliveryZoneConflicts.FindAsync(_context, tenantId, zone, cancellationToken) is { } meanwhile)
                return Result.Failure<DeliveryZoneDto>(meanwhile);
            throw;
        }

        return Result.Success(await DeliveryZoneReader.LoadAsync(_context, zone.Id, cancellationToken));
    }
}

public class DeleteDeliveryZoneCommandHandler : IRequestHandler<DeleteDeliveryZoneCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUserService;

    public DeleteDeliveryZoneCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUserService = currentUserService;
    }

    public async Task<Result> Handle(DeleteDeliveryZoneCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.TenantId;
        var zone = await _context.DeliveryZones
            .Include(z => z.PostalCodes)
            .FirstOrDefaultAsync(z => z.Id == request.Id && z.TenantId == tenantId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(DeliveryZone), request.Id);
        if (zone.Revision != request.Revision)
            return Result.Failure(DeliveryZoneErrors.Stale);

        _context.AuditLogs.Add(DeliveryZoneAudit.Create(tenantId, zone, DeliveryZoneAudit.Deleted, null, _currentUserService.UserName));
        _context.DeliveryZones.Remove(zone);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await _context.DiscardUnitOfWorkAsync(cancellationToken);
            return Result.Failure(DeliveryZoneErrors.Stale);
        }

        return Result.Success();
    }
}

/// <summary>
/// A name and a postal code are in one zone of the business at most: what a save would take from another zone, and
/// whether the zone is still the revision a save was made over.
/// </summary>
internal static class DeliveryZoneConflicts
{
    /// <summary>Why <paramref name="zone"/> cannot be saved as it is: its name or one of its postal codes is in another zone.</summary>
    public static async Task<string?> FindAsync(
        IApplicationDbContext context, Guid tenantId, DeliveryZone zone, CancellationToken cancellationToken)
    {
        if (await context.DeliveryZones.AsNoTracking()
                .AnyAsync(other => other.TenantId == tenantId && other.Id != zone.Id && other.Name == zone.Name, cancellationToken))
            return DeliveryZoneErrors.NameTaken(zone.Name);
        return await PostalCodeTakenAsync(context, tenantId, zone, cancellationToken);
    }

    /// <summary>The zone is saved with <paramref name="revision"/>: nobody saved or deleted it since it was read.</summary>
    public static Task<bool> StillAtAsync(
        IApplicationDbContext context, Guid tenantId, Guid zoneId, int revision, CancellationToken cancellationToken)
        => context.DeliveryZones.AsNoTracking()
            .AnyAsync(zone => zone.Id == zoneId && zone.TenantId == tenantId && zone.Revision == revision, cancellationToken);

    /// <summary>The message naming the first postal code of <paramref name="zone"/> another zone has, or null.</summary>
    private static async Task<string?> PostalCodeTakenAsync(
        IApplicationDbContext context, Guid tenantId, DeliveryZone zone, CancellationToken cancellationToken)
    {
        var codes = zone.PostalCodes.Select(code => code.PostalCode).ToList();
        var taken = await context.DeliveryZonePostalCodes
            .AsNoTracking()
            .Where(code => code.TenantId == tenantId && code.DeliveryZoneId != zone.Id && codes.Contains(code.PostalCode))
            .Join(context.DeliveryZones.AsNoTracking(), code => code.DeliveryZoneId, other => other.Id,
                (code, other) => new { code.PostalCode, other.Name })
            .OrderBy(row => row.PostalCode)
            .FirstOrDefaultAsync(cancellationToken);
        return taken is null ? null : DeliveryZoneErrors.PostalCodeInAnotherZone(taken.PostalCode, taken.Name);
    }
}

/// <summary>
/// The activity entry of a zone: what it charges and how many postal codes it reaches, before (for a change) and after.
/// Never which postal codes: a zone can have hundreds.
/// </summary>
internal static class DeliveryZoneAudit
{
    public const string Created = "DeliveryZoneCreated";
    public const string Updated = "DeliveryZoneUpdated";
    public const string Deleted = "DeliveryZoneDeleted";

    internal sealed record State(
        string Name, decimal Fee, decimal MinimumOrder, decimal? FreeDeliveryFrom, bool IsActive, int PostalCodes, int Revision);

    public static State Describe(DeliveryZone zone) => new(
        zone.Name, zone.Fee, zone.MinimumOrder, zone.FreeDeliveryFrom, zone.IsActive, zone.PostalCodes.Count, zone.Revision);

    public static AuditLog Create(Guid tenantId, DeliveryZone zone, string action, State? previous, string? performedBy)
        => new(
            tenantId: tenantId,
            entityName: nameof(DeliveryZone),
            entityId: zone.Id.ToString(),
            action: action,
            performedBy: performedBy ?? "system",
            metadata: JsonSerializer.Serialize(new { Previous = previous, New = Describe(zone) }));
}
