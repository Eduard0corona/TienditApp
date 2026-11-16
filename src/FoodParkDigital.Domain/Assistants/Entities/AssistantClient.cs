using System.Text;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Assistants.Entities;

/// <summary>
/// An AI assistant's application registered to ask a customer for their account (Fase 32E, ADR 0097): an OAuth public client
/// of the dynamic registration (RFC 7591), with no secret. Anyone may register one; it gets nothing until a customer allows
/// it, and then only for the account that did. Its name is what the assistant says it is: the permission page shows it with
/// the site the code returns to, which is what the customer can check.
/// </summary>
public class AssistantClient : AuditableEntity, IAggregateRoot
{
    public const int MaxNameLength = 100;
    public const int MaxRedirectUris = 10;
    public const int MaxRedirectUriLength = 2000;

    /// <summary>How long a client no account connected stays registered.</summary>
    public static readonly TimeSpan TimeToConnect = TimeSpan.FromDays(7);

    public const string ClientIdRequiredMessage = "El cliente necesita su identificador.";
    public const string RedirectUrisRequiredMessage = "Registra al menos una dirección de regreso (redirect_uri).";
    public const string TooManyRedirectUrisMessage = "Registra hasta 10 direcciones de regreso.";
    public const string InvalidRedirectUriMessage =
        "Cada dirección de regreso es https, o http a localhost, sin fragmento ni usuario.";

    private List<string> _redirectUris = [];

    private AssistantClient() { }

    /// <summary>Its public identifier (client_id): random, so one client cannot guess another's.</summary>
    public string ClientId { get; private set; } = string.Empty;

    /// <summary>The name it gave (client_name), without control characters; null when it gave none.</summary>
    public string? Name { get; private set; }

    /// <summary>Where it may receive a code: the redirect_uri of a request is one of these, exactly.</summary>
    public IReadOnlyList<string> RedirectUris => _redirectUris;

    public DateTimeOffset RegisteredAt { get; private set; }

    public static AssistantClient Register(string clientId, string? name, IEnumerable<string>? redirectUris, DateTimeOffset now)
    {
        if (!AssistantSecrets.IsWellFormed(clientId))
            throw new DomainException(ClientIdRequiredMessage);
        var uris = (redirectUris ?? []).Distinct(StringComparer.Ordinal).ToList();
        if (uris.Count == 0)
            throw new DomainException(RedirectUrisRequiredMessage);
        if (uris.Count > MaxRedirectUris)
            throw new DomainException(TooManyRedirectUrisMessage);
        if (!uris.All(IsRedirectUri))
            throw new DomainException(InvalidRedirectUriMessage);

        return new AssistantClient
        {
            ClientId = clientId,
            Name = NameOf(name),
            _redirectUris = uris,
            RegisteredAt = now
        };
    }

    /// <summary>
    /// A name to show: trimmed, with spaces for its control characters and line breaks, up to <see cref="MaxNameLength"/>.
    /// </summary>
    public static string? NameOf(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        var text = new StringBuilder(name.Length);
        foreach (var c in name)
            text.Append(char.IsControl(c) || char.IsWhiteSpace(c) ? ' ' : c);
        var kept = string.Join(' ', text.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (kept.Length > MaxNameLength)
            kept = kept[..MaxNameLength].TrimEnd();
        return kept.Length == 0 ? null : kept;
    }

    /// <summary>
    /// Whether <paramref name="uri"/> may receive codes: https, or http to this device (localhost, 127.0.0.1 or [::1], the
    /// address of a desktop app); absolute, without a fragment and without a user before the host, so the site the
    /// permission page shows is the one it goes to.
    /// </summary>
    public static bool IsRedirectUri(string? uri)
    {
        if (string.IsNullOrEmpty(uri) || uri.Length > MaxRedirectUriLength || uri.Contains('#')
            || uri.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            return false;
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || parsed.UserInfo.Length > 0 || parsed.Host.Length == 0)
            return false;
        if (parsed.Scheme == Uri.UriSchemeHttps)
            return true;
        return parsed.Scheme == Uri.UriSchemeHttp && IsLoopback(parsed);
    }

    /// <summary>
    /// Whether a request may send its code to <paramref name="uri"/>: one of its addresses, exactly. On this device (http to
    /// localhost) the port may differ, as RFC 8252 asks: a desktop app listens on whichever port is free.
    /// </summary>
    public bool AllowsRedirect(string? uri)
    {
        if (!IsRedirectUri(uri))
            return false;
        if (_redirectUris.Contains(uri!, StringComparer.Ordinal))
            return true;

        var asked = new Uri(uri!);
        if (asked.Scheme != Uri.UriSchemeHttp)
            return false;
        return _redirectUris.Select(registered => new Uri(registered)).Any(registered =>
            registered.Scheme == Uri.UriSchemeHttp
            && string.Equals(registered.Host, asked.Host, StringComparison.OrdinalIgnoreCase)
            && string.Equals(PathAndQuery(registered), PathAndQuery(asked), StringComparison.Ordinal));
    }

    private static bool IsLoopback(Uri uri) => uri.HostNameType == UriHostNameType.Dns
        ? string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
        : uri.IsLoopback;

    private static string PathAndQuery(Uri uri) => uri.GetComponents(UriComponents.PathAndQuery, UriFormat.UriEscaped);
}
