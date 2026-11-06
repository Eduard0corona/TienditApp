using System.Net;
using FoodParkDigital.Application.Messaging;

namespace FoodParkDigital.Application.Common.Notices;

/// <summary>
/// How the emails that tell the business of what its customers did online write (Fases 29N and 29O, ADR 0068 and 0069): the
/// dates and times in the business's zone, and what the business or the customer typed on one line and encoded.
/// </summary>
internal static class NoticeEmailText
{
    /// <summary>"jueves 6 de noviembre a las 10:00 a. m.", or "a la 1:00 p. m.", in the business's zone.</summary>
    public static string When(DateTimeOffset at, string? timeZoneId)
    {
        var local = MessageValues.ToLocal(at, timeZoneId);
        return $"{MessageValues.FormatDate(local)} {(local.Hour % 12 == 1 ? "a la" : "a las")} {MessageValues.FormatTime(local)}";
    }

    /// <summary>Typed by the business or by the customer: one line.</summary>
    public static string OneLine(string value) => value.ReplaceLineEndings(" ").Trim();

    public static string Html(string value) => WebUtility.HtmlEncode(value);

    /// <summary>The app's address without its last slash, or null when it is not configured (then the emails carry no links).</summary>
    public static string? App(string? appUrl) => appUrl?.TrimEnd('/');

    /// <summary>Why it arrives, and where to stop it: Mi perfil, linked when the app's address is known.</summary>
    public static string Footer(string why, string? app)
    {
        var profile = app is null ? "Mi perfil" : $"<a href='{app}/profile'>Mi perfil</a>";
        return $"<p style='color:#6c757d;font-size:12px'>Te llega porque {why}. Puedes dejar de recibirlos en {profile}.</p>";
    }
}
