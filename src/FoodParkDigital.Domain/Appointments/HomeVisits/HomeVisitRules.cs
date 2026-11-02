using System.Globalization;
using FoodParkDigital.Domain.Appointments.Enums;

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

    public const string ChargePolicyMessage = "Elige cómo cobras la visita: se cobra, sin costo o se descuenta si contratan.";
    public const string VisitServiceMessage = "Elige un servicio de tu catálogo como la visita.";

    /// <summary>The name of a visit to quote requested online when the business picked no service as its visit (Fase 29B).</summary>
    public const string DefaultVisitName = "Visita a domicilio";

    /// <summary>What the customer writes about what happens, in a visit requested online (Fase 29B).</summary>
    public const int MaxProblemLength = 500;
    public const string ProblemRequiredMessage = "Cuéntanos qué pasa para preparar la visita.";
    public const string ProblemTooLongMessage = "Lo que pasa admite hasta 500 caracteres.";

    /// <summary>The photos of what happens a visit requested online receives from its link (Fase 29H, ADR 0062).</summary>
    public const int MaxProblemPhotos = 5;
    public const string ProblemPhotosFullMessage = "Tu visita ya tiene sus 5 fotos.";
    public const string ProblemPhotosClosedMessage =
        "Tu visita ya no recibe fotos: ya pasó su hora, se canceló o ya se atendió.";
    public const string ProblemPhotosNotAVisitMessage = "Solo una visita a domicilio pedida en línea recibe fotos de lo que pasa.";

    /// <summary>
    /// Why a visit takes no more photos of what happens from its link (Fase 29H, ADR 0062), or null when it takes one more:
    /// a visit to quote the customer requested online (not the job of a quote), «Por confirmar» or agendada, before its
    /// time, with fewer than <see cref="MaxProblemPhotos"/>.
    /// </summary>
    /// <param name="photosSoFar">How many it already has.</param>
    public static string? ProblemPhotoRefusal(
        AppointmentChannel channel, bool homeVisit, bool work, AppointmentStatus status, DateTimeOffset startAt, int photosSoFar,
        DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(photosSoFar);
        if (channel != AppointmentChannel.Online || !homeVisit || work)
            return ProblemPhotosNotAVisitMessage;
        if (status is not (AppointmentStatus.Requested or AppointmentStatus.Scheduled or AppointmentStatus.Confirmed) || startAt <= now)
            return ProblemPhotosClosedMessage;
        return photosSoFar >= MaxProblemPhotos ? ProblemPhotosFullMessage : null;
    }

    /// <summary>«Por ahora no hacemos visitas en el 72000.»</summary>
    public static string NotCoveredMessage(string postalCode)
        => string.Create(CultureInfo.InvariantCulture, $"Por ahora no hacemos visitas en el {postalCode}.");

    /// <summary>«Ana López necesita 30 minutos de traslado entre una visita a domicilio y su otra cita.»</summary>
    public static string TravelMessage(string staffName, int travelMinutes)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{staffName} necesita {travelMinutes} minutos de traslado entre una visita a domicilio y su otra cita.");
}
