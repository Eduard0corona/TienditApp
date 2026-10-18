using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Common.ValueObjects;

/// <summary>
/// A point on the map in degrees, as GPS and Google Maps write it (WGS 84): latitude first, then longitude. Kept to six
/// decimals, about 11 cm.
/// </summary>
public sealed record GeoPoint
{
    public const int Decimals = 6;
    private const double EarthRadiusKm = 6371.0088;

    public const string InvalidMessage = "Las coordenadas no son válidas: la latitud va de -90 a 90 y la longitud de -180 a 180.";
    public const string NullIslandMessage = "Las coordenadas 0, 0 están en medio del mar. Copia las de tu local en Google Maps.";

    public GeoPoint(decimal latitude, decimal longitude)
    {
        if (latitude is < -90m or > 90m || longitude is < -180m or > 180m)
            throw new DomainException(InvalidMessage);
        // A device or a form that had no position writes 0, 0: no business is there.
        if (latitude == 0m && longitude == 0m)
            throw new DomainException(NullIslandMessage);

        Latitude = Math.Round(latitude, Decimals, MidpointRounding.AwayFromZero);
        Longitude = Math.Round(longitude, Decimals, MidpointRounding.AwayFromZero);
    }

    public decimal Latitude { get; }
    public decimal Longitude { get; }

    /// <summary>The point with fewer decimals: 3 is about 110 m, the grid a customer's position travels in (Fase 24G).</summary>
    public GeoPoint RoundedTo(int decimals)
        => new(Math.Round(Latitude, decimals, MidpointRounding.AwayFromZero), Math.Round(Longitude, decimals, MidpointRounding.AwayFromZero));

    /// <summary>
    /// The distance in a straight line over the Earth, in kilometers (haversine on its mean radius): within a few meters
    /// across a city, not the way by its streets.
    /// </summary>
    public double KilometersTo(GeoPoint other)
    {
        var lat1 = Radians(Latitude);
        var lat2 = Radians(other.Latitude);
        var dLat = lat2 - lat1;
        var dLon = Radians(other.Longitude) - Radians(Longitude);
        var h = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    private static double Radians(decimal degrees) => (double)degrees * Math.PI / 180;
}
