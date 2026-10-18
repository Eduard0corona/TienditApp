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
        // Rounded first: what is checked is what is kept, so 0.0000001, 0.0000002 is the 0, 0 it becomes.
        latitude = Round(latitude);
        longitude = Round(longitude);
        if (Problem(latitude, longitude) is { } problem)
            throw new DomainException(problem);

        Latitude = latitude;
        Longitude = longitude;
    }

    /// <summary>
    /// Why those degrees, rounded to <see cref="Decimals"/> decimals as a point keeps them, are not a point on the map;
    /// null when they are. The one rule of the validators, of a new point and of a row read back.
    /// </summary>
    public static string? Problem(decimal latitude, decimal longitude)
    {
        latitude = Round(latitude);
        longitude = Round(longitude);
        if (latitude is < -90m or > 90m || longitude is < -180m or > 180m)
            return InvalidMessage;
        // A device or a form that had no position writes 0, 0: no business is there.
        return latitude == 0m && longitude == 0m ? NullIslandMessage : null;
    }

    /// <summary>
    /// The point a row keeps; null when it has none, or when what it has is not a point (written before the database
    /// refused it, or by hand): a bad row is left off the map instead of breaking the page that reads it.
    /// </summary>
    public static GeoPoint? FromStored(decimal? latitude, decimal? longitude)
        => latitude is { } lat && longitude is { } lng && Problem(lat, lng) is null ? new GeoPoint(lat, lng) : null;

    public decimal Latitude { get; }
    public decimal Longitude { get; }

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

    private static decimal Round(decimal degrees) => Math.Round(degrees, Decimals, MidpointRounding.AwayFromZero);

    private static double Radians(decimal degrees) => (double)degrees * Math.PI / 180;
}
