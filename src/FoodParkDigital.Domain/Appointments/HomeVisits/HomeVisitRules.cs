using System.Globalization;

namespace FoodParkDigital.Domain.Appointments.HomeVisits;

/// <summary>
/// When an appointment happens at the customer's address and what is said when it may not (Fase 22E): the zones are the
/// delivery zones of the business, and the visit charges what its zone charges to go there; the team member needs the
/// travel time of the business between a visit and their other appointments; the charge enters the sale of the appointment.
/// </summary>
public static class HomeVisitRules
{
    /// <summary>The travel time a business starts with between a visit and another appointment of the same person.</summary>
    public const int DefaultTravelMinutes = 30;
    public const int MaxTravelMinutes = 240;
    public const string TravelMinutesMessage = "El traslado entre visitas va de 0 a 240 minutos.";

    public const string NoZonesMessage =
        "Para citas a domicilio, primero da de alta tus zonas en Configuración › Zonas de entrega.";

    public const string CancelledSaleMessage = "La cita se canceló: ya no se cobra.";
    public const string NoShowSaleMessage = "El cliente no se presentó a la cita: no se cobra.";
    public const string AlreadySoldMessage = "La cita ya tiene su venta.";

    public const string AlreadyChargedMessage = "La venta ya lleva el cargo por visita.";
    public const string OnlyNewOrdersMessage = "El cargo por visita va en una venta nueva.";
    public const string NotForPlatformsMessage = "Un pedido de Rappi, Uber Eats o DiDi no lleva cargo por visita.";
    public const string NotForTabsMessage = "Una cuenta de mesa no lleva cargo por visita.";
    public const string NotForDeliveriesMessage = "Un pedido a domicilio lleva envío, no cargo por visita.";
    public const string NeedsItemsMessage = "Agrega el servicio antes del cargo por visita.";
    public const string BeforeAnyPaymentMessage = "El cargo por visita se agrega antes de cobrar.";
    public const string FeeMessage = "El cargo por visita va de $0.01 a $10,000.";

    /// <summary>«Por ahora no hacemos visitas en el 72000.»</summary>
    public static string NotCoveredMessage(string postalCode)
        => string.Create(CultureInfo.InvariantCulture, $"Por ahora no hacemos visitas en el {postalCode}.");

    /// <summary>«Ana López necesita 30 minutos de traslado entre una visita a domicilio y su otra cita.»</summary>
    public static string TravelMessage(string staffName, int travelMinutes)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{staffName} necesita {travelMinutes} minutos de traslado entre una visita a domicilio y su otra cita.");
}
