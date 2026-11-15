namespace FoodParkDigital.Domain.Appointments.OnlineBooking;

/// <summary>
/// The deposit a customer pays online to book an appointment (Fase 31O, ADR 0095): the business's share of the service's
/// price, paid in Mercado Pago before the request exists. It is held until the sale of the appointment takes it, and goes
/// back or stays with the business as these rules say.
/// </summary>
public static class BookingDeposits
{
    /// <summary>The share of the service's price a business may ask, in percent.</summary>
    public const int MinPercent = 10;
    public const int MaxPercent = 100;

    /// <summary>A deposit below it is not asked: that service is booked as before.</summary>
    public const decimal MinDeposit = 10m;

    /// <summary>
    /// A customer who cancels at least this long before an agendada appointment gets the deposit back; later, the business
    /// keeps it. A request not answered yet, or one the business moved to another time, always gets it back.
    /// </summary>
    public static readonly TimeSpan FreeCancellationNotice = TimeSpan.FromHours(24);

    public const string PercentMessage = "El anticipo va del 10 % al 100 % del precio del servicio.";
    public const string NothingToReturnMessage = "Esta cita no tiene un anticipo por devolver.";
    public const string AboveSaleMessage =
        "El anticipo que el cliente pagó en línea es mayor que la venta: cobra al menos el anticipo y, si fue de más, reembolsa la diferencia «En línea».";

    /// <summary>
    /// Whether its customer cancelling an appointment now gets its deposit back: one held, of a request not answered yet, of
    /// one the business moved from the time it was asked for, or of one at least <see cref="FreeCancellationNotice"/> away.
    /// </summary>
    public static bool CustomerCancelReturns(
        Enums.AppointmentDepositStatus? deposit,
        Enums.AppointmentStatus status,
        DateTimeOffset? requestedStartAt,
        DateTimeOffset startAt,
        DateTimeOffset now)
        => deposit == Enums.AppointmentDepositStatus.Held
            && (status == Enums.AppointmentStatus.Requested
                || (requestedStartAt is { } askedFor && askedFor != startAt)
                || startAt - now >= FreeCancellationNotice);

    /// <summary>
    /// The deposit a service asks: <paramref name="percent"/> of its <paramref name="price"/>, to the cent, half away from
    /// zero; none without a percent or below <see cref="MinDeposit"/>.
    /// </summary>
    public static decimal? For(int? percent, decimal price)
    {
        if (percent is not { } share)
            return null;
        if (share is < MinPercent or > MaxPercent)
            throw new Exceptions.DomainException(PercentMessage);
        var deposit = decimal.Round(price * share / 100m, 2, MidpointRounding.AwayFromZero);
        return deposit < MinDeposit ? null : deposit;
    }
}
