using FoodParkDigital.Domain.Common.ValueObjects;

namespace FoodParkDigital.Application.Common.Models;

/// <summary>A point on the map in degrees, latitude first (Fase 24G): to open it in a map app or tell search engines.</summary>
public class MapPointDto
{
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }

    public static MapPointDto? From(GeoPoint? point)
        => point is null ? null : new MapPointDto { Latitude = point.Latitude, Longitude = point.Longitude };
}
