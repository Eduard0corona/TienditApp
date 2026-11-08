using FoodParkDigital.Application.Common.Notices;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Appointments.OnlineBooking;

namespace FoodParkDigital.Application.Appointments.RequestNotices;

/// <summary>
/// The email that tells a member of a request online (Fase 29N, ADR 0068): what was asked for, when, by whom and by when to
/// answer it, with the link to the agenda. Never the customer's phone, notes, address or photos: those stay in the agenda.
/// </summary>
internal static class RequestEmail
{
    /// <summary>Past this after it arrived, the email is not sent: whoever answers has seen it in the agenda or the bell.</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromDays(1);

    /// <summary>A request is worth an email while it waits, it arrived less than <see cref="MaxDelay"/> ago and its time has not come.</summary>
    public static bool Worth(AppointmentStatus status, DateTimeOffset arrivedAt, DateTimeOffset startAt, DateTimeOffset now)
        => status == AppointmentStatus.Requested && now - arrivedAt <= MaxDelay && startAt > now;

    /// <summary>
    /// A request is worth its reminder (Fase 29U, ADR 0075) while it still waits and its limit to be answered in time has not
    /// come: past it, the reminder would only say it is late.
    /// </summary>
    /// <param name="askedFor">The time its customer asked for (<c>RequestedStartAt</c>, or its time when it has none).</param>
    public static bool ReminderWorth(AppointmentStatus status, DateTimeOffset arrivedAt, DateTimeOffset askedFor, DateTimeOffset now)
        => status == AppointmentStatus.Requested && now < ResponseTimes.AnswerBy(arrivedAt, askedFor);

    /// <summary>
    /// The reminder of a request still waiting before its limit (Fase 29U, ADR 0075; as long before as its business picked
    /// since the Fase 30B, ADR 0082): what was asked for, when and by
    /// whom, by when to answer it, and the link to the agenda. Never the customer's phone, notes, address or photos.
    /// </summary>
    public static RequestEmailContent ComposeReminder(
        string businessName,
        string customerName,
        string serviceName,
        bool homeVisit,
        DateTimeOffset startAt,
        DateTimeOffset answerBy,
        string? timeZoneId,
        string? appUrl)
    {
        var business = OneLine(businessName);
        var customer = OneLine(customerName);
        var service = OneLine(serviceName);
        var kind = homeVisit ? "visita" : "cita";
        var when = NoticeEmailText.When(startAt, timeZoneId);
        var app = NoticeEmailText.App(appUrl);

        var subject = $"Responde a tiempo la solicitud de {kind} en {business}: {service}, {when}";
        var body = string.Join("\n",
            $"<h2>Una solicitud de {kind} sigue sin respuesta</h2>",
            Asked(homeVisit, customer, service, business, when),
            $"<p>Respóndela antes del {NoticeEmailText.When(answerBy, timeZoneId)} para que cuente como respondida a tiempo.</p>",
            Open(app),
            Footer(business, app));
        return new RequestEmailContent(subject, body);
    }

    public static RequestEmailContent Compose(
        string businessName,
        string customerName,
        string serviceName,
        bool homeVisit,
        DateTimeOffset startAt,
        DateTimeOffset answerBy,
        DateTimeOffset now,
        string? timeZoneId,
        string? appUrl)
    {
        // Typed by the business and by the customer: one line each, and encoded in the body.
        var business = OneLine(businessName);
        var customer = OneLine(customerName);
        var service = OneLine(serviceName);
        var kind = homeVisit ? "visita" : "cita";
        var when = NoticeEmailText.When(startAt, timeZoneId);
        var app = NoticeEmailText.App(appUrl);

        var subject = $"Nueva solicitud de {kind} en {business}: {service}, {when}";
        var asked = Asked(homeVisit, customer, service, business, when);
        var limit = answerBy > now
            ? $"<p>Respóndela antes del {NoticeEmailText.When(answerBy, timeZoneId)} para que cuente como respondida a tiempo.</p>"
            : null;
        var body = string.Join("\n", new[] { $"<h2>Nueva solicitud de {kind}</h2>", asked, limit, Open(app), Footer(business, app) }.OfType<string>());
        return new RequestEmailContent(subject, body);
    }

    /// <summary>Who asked for what, and when. The time ends with "a. m." or "p. m.": no period after it.</summary>
    private static string Asked(bool homeVisit, string customer, string service, string business, string when)
        => homeVisit
            ? $"<p><strong>{Html(customer)}</strong> pidió una visita a domicilio{(service == HomeVisitRules.DefaultVisitName ? "" : $" ({Html(service)})")} a {Html(business)} para el {when}</p>"
            : $"<p><strong>{Html(customer)}</strong> pidió <strong>{Html(service)}</strong> en {Html(business)} para el {when}</p>";

    private static string Open(string? app)
        => app is null
            ? "<p>Respóndela desde la Agenda de TienditApp.</p>"
            : $"<p><a href='{app}/appointments'>Abrir la Agenda</a></p>";

    /// <summary>
    /// Why the agenda's emails arrive (Fases 29N, 29O and 29U): the member gets one with each request online, its reminder if it
    /// still waits before its limit (as long before as the business picked), and one with each appointment its customer cancels.
    /// </summary>
    internal static string Footer(string business, string? app)
        => NoticeEmailText.Footer(
            $"recibes un correo con cada solicitud en línea de {Html(business)}, un recordatorio de las que siguen sin respuesta y uno con cada cita que sus clientes cancelan", app);

    private static string OneLine(string value) => NoticeEmailText.OneLine(value);

    private static string Html(string value) => NoticeEmailText.Html(value);
}

internal sealed record RequestEmailContent(string Subject, string HtmlBody);
