using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Delivery;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Delivery;

/// <summary>
/// The delivery zones of the business, by name: everyone on the team sees them, as whoever takes an order needs to know
/// where it delivers and what it charges. Changing them is for owners and admins. A business has up to
/// <see cref="DeliveryZone.MaxZonesPerBusiness"/>, so the list has no pages.
/// </summary>
[RequiresTenantMember]
public record GetDeliveryZonesQuery : IRequest<List<DeliveryZoneDto>>;

public class GetDeliveryZonesQueryHandler : IRequestHandler<GetDeliveryZonesQuery, List<DeliveryZoneDto>>
{
    private readonly IApplicationDbContext _context;

    public GetDeliveryZonesQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public Task<List<DeliveryZoneDto>> Handle(GetDeliveryZonesQuery request, CancellationToken cancellationToken)
        => DeliveryZoneReader.ListAsync(_context, cancellationToken);
}

internal static class DeliveryZoneReader
{
    public static Task<List<DeliveryZoneDto>> ListAsync(IApplicationDbContext context, CancellationToken cancellationToken)
        => context.DeliveryZones
            .AsNoTracking()
            .OrderBy(zone => zone.Name)
            .Select(DeliveryZoneDto.Projection)
            .ToListAsync(cancellationToken);

    public static Task<DeliveryZoneDto> LoadAsync(IApplicationDbContext context, Guid zoneId, CancellationToken cancellationToken)
        => context.DeliveryZones
            .AsNoTracking()
            .Where(zone => zone.Id == zoneId)
            .Select(DeliveryZoneDto.Projection)
            .SingleAsync(cancellationToken);
}

/// <summary>Where the business delivers, for whoever answers a customer: the zone of a postal code.</summary>
public static class DeliveryZoneLookup
{
    /// <summary>Whether the business has a zone that delivers now.</summary>
    public static Task<bool> AnyActiveAsync(IApplicationDbContext context, Guid tenantId, CancellationToken cancellationToken)
        => context.DeliveryZones.AsNoTracking().AnyAsync(zone => zone.TenantId == tenantId && zone.IsActive, cancellationToken);

    /// <summary>The active zone of the business with <paramref name="postalCode"/>, or null when it does not deliver there.</summary>
    public static async Task<DeliveryZoneCoverage?> FindAsync(
        IApplicationDbContext context, Guid tenantId, string? postalCode, CancellationToken cancellationToken)
    {
        var code = DeliveryZone.NormalizePostalCode(postalCode);
        if (!DeliveryZone.IsPostalCode(code))
            return null;

        return await context.DeliveryZones
            .AsNoTracking()
            .Where(zone => zone.TenantId == tenantId && zone.IsActive && zone.PostalCodes.Any(existing => existing.PostalCode == code))
            .Select(zone => new DeliveryZoneCoverage(zone.Name, zone.Fee, zone.MinimumOrder, zone.FreeDeliveryFrom))
            .FirstOrDefaultAsync(cancellationToken);
    }
}

/// <summary>What a zone charges, for a customer who asks: its name, fee, minimum and free delivery.</summary>
public sealed record DeliveryZoneCoverage(string Name, decimal Fee, decimal MinimumOrder, decimal? FreeDeliveryFrom);
