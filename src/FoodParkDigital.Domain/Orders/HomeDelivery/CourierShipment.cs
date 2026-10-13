using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;

namespace FoodParkDigital.Domain.Orders.HomeDelivery;

/// <summary>
/// The trip a courier makes with an order, as the business writes it down (Fase 22C): today only Paquetenvia, with the
/// folio of its trip, normalized as a platform's folio is (<see cref="PlatformOrderNumber"/>), and, optionally, the link
/// that follows it. The link is kept only when it is a secure web address, so it can be opened and shared as it is.
/// </summary>
public sealed record CourierShipment
{
    public const int MaxTrackingUrlLength = 500;

    public const string FolioRequiredMessage = "Escribe el folio del envío de Paquetenvia.";
    public const string FolioInvalidMessage = "El folio del envío lleva de 1 a 30 letras, números o guiones.";
    public const string TrackingUrlMessage = "El enlace de seguimiento empieza con https:// y lleva hasta 500 caracteres.";
    public const string CourierMessage = "Elige con qué repartidor va el pedido.";

    private CourierShipment(DeliveryCourier courier, string folio, string? trackingUrl)
    {
        Courier = courier;
        Folio = folio;
        TrackingUrl = trackingUrl;
    }

    public DeliveryCourier Courier { get; }
    public string Folio { get; }
    public string? TrackingUrl { get; }

    /// <summary>The trip as written, normalized, or a <see cref="DomainException"/> saying what is wrong.</summary>
    public static CourierShipment From(DeliveryCourier courier, string? folio, string? trackingUrl)
    {
        if (!Enum.IsDefined(courier))
            throw new DomainException(CourierMessage);
        if (string.IsNullOrWhiteSpace(folio))
            throw new DomainException(FolioRequiredMessage);
        if (!PlatformOrderNumber.TryNormalize(folio, out var normalized))
            throw new DomainException(FolioInvalidMessage);

        return new CourierShipment(courier, normalized, NormalizeTrackingUrl(trackingUrl));
    }

    private const string HttpsPrefix = "https://";

    /// <summary>
    /// The link trimmed, with «https://» in lower case, or none when blank; a <see cref="DomainException"/> when it is not
    /// an https address. It must start with «https://» as written: <see cref="Uri"/> also reads «https:\\host», which the
    /// database would refuse (CK_OrderDeliveries_TrackingUrl).
    /// </summary>
    public static string? NormalizeTrackingUrl(string? trackingUrl)
    {
        if (string.IsNullOrWhiteSpace(trackingUrl))
            return null;

        var url = trackingUrl.Trim();
        if (!url.StartsWith(HttpsPrefix, StringComparison.OrdinalIgnoreCase))
            throw new DomainException(TrackingUrlMessage);
        url = HttpsPrefix + url[HttpsPrefix.Length..];
        if (url.Length > MaxTrackingUrlLength
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo))
            throw new DomainException(TrackingUrlMessage);

        return url;
    }
}
