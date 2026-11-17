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
    public const int MaxRedirectUris = 5;
    public const int MaxRedirectUriLength = 512;

    /// <summary>
    /// The longest its addresses take stored together (a JSON list): each one with its quotes and a comma, and the brackets.
    /// </summary>
    public const int MaxRedirectUrisStoredLength = MaxRedirectUris * (MaxRedirectUriLength + 3) + 2;

    /// <summary>How long a client no account connected stays registered: an assistant registers one when it connects.</summary>
    public static readonly TimeSpan TimeToConnect = TimeSpan.FromDays(1);

    /// <summary>How long one that was connected stays registered after its last connection, once it has none.</summary>
    public static readonly TimeSpan KeptAfterLastConnection = TimeSpan.FromDays(365);

    /// <summary>
    /// The clients registered in an hour, from every address: anyone registers one, and each is kept a day, so this is
    /// what bounds the table. Far more than the assistants that connect in an hour.
    /// </summary>
    public const int MaxRegisteredPerHour = 500;

    public const string ClientIdRequiredMessage = "El cliente necesita su identificador.";
    public const string RedirectUrisRequiredMessage = "Registra al menos una dirección de regreso (redirect_uri).";
    public const string TooManyRedirectUrisMessage = "Registra hasta 5 direcciones de regreso.";
    public const string InvalidRedirectUriMessage =
        "Cada dirección de regreso es https, o http a localhost, sin fragmento ni usuario, de hasta 512 caracteres.";

    private List<string> _redirectUris = [];

    private AssistantClient() { }

    /// <summary>Its public identifier (client_id): random, so one client cannot guess another's.</summary>
    public string ClientId { get; private set; } = string.Empty;

    /// <summary>The name it gave (client_name), without control characters; null when it gave none.</summary>
    public string? Name { get; private set; }

    /// <summary>Where it may receive a code: the redirect_uri of a request is one of these, exactly.</summary>
    public IReadOnlyList<string> RedirectUris => _redirectUris;

    public DateTimeOffset RegisteredAt { get; private set; }

    /// <summary>When an account last connected it (a code exchanged); null while none has.</summary>
    public DateTimeOffset? LastConnectedAt { get; private set; }

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

    /// <summary>An account connected it now: it stays registered while it is used.</summary>
    public void RecordConnection(DateTimeOffset now)
    {
        if (LastConnectedAt is null || now > LastConnectedAt)
            LastConnectedAt = now;
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
    /// permission page shows is the one it goes to. Written in printable ASCII (an international host goes in punycode),
    /// without quotes or backslashes, so it is stored as written, in <see cref="MaxRedirectUrisStoredLength"/>.
    /// </summary>
    public static bool IsRedirectUri(string? uri)
    {
        if (string.IsNullOrEmpty(uri) || uri.Length > MaxRedirectUriLength || uri.Contains('#')
            || uri.Any(c => c is <= ' ' or > '~' or '"' or '\\'))
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

    /// <summary>The three names of this device that the permission page also returns to; not the rest of 127.0.0.0/8.</summary>
    private static bool IsLoopback(Uri uri) => uri.HostNameType switch
    {
        UriHostNameType.Dns => string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase),
        UriHostNameType.IPv4 => uri.Host == "127.0.0.1",
        UriHostNameType.IPv6 => uri.Host == "[::1]",
        _ => false
    };

    private static string PathAndQuery(Uri uri) => uri.GetComponents(UriComponents.PathAndQuery, UriFormat.UriEscaped);
}
