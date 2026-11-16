using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using FoodParkDigital.Domain.Assistants.Entities;

namespace FoodParkDigital.Application.Assistants;

/// <summary>The error codes of OAuth (RFC 6749, 7009, 7591 and 8707) an assistant reads.</summary>
public static class OAuthErrors
{
    public const string InvalidRequest = "invalid_request";
    public const string InvalidClient = "invalid_client";
    public const string InvalidGrant = "invalid_grant";
    public const string UnsupportedGrantType = "unsupported_grant_type";
    public const string UnsupportedResponseType = "unsupported_response_type";
    public const string InvalidScope = "invalid_scope";
    public const string InvalidTarget = "invalid_target";
    public const string AccessDenied = "access_denied";
    public const string InvalidRedirectUri = "invalid_redirect_uri";
    public const string InvalidClientMetadata = "invalid_client_metadata";

    /// <summary>The server takes no more for now (RFC 6749, 4.1.2.1): the registration of clients answers it when full.</summary>
    public const string TemporarilyUnavailable = "temporarily_unavailable";
}

/// <summary>
/// The rules of the authorization server an assistant meets (Fase 32E, ADR 0097): only the code flow with PKCE S256, public
/// clients, the scope <see cref="AssistantConnection.AccountScope"/> and the account's MCP server as the only resource.
/// </summary>
public static class AssistantAuthorization
{
    public const string ResponseType = "code";
    public const string ChallengeMethod = "S256";
    public const string AuthorizationCodeGrant = "authorization_code";
    public const string RefreshTokenGrant = "refresh_token";
    public const string ClientAuthenticationMethod = "none";

    /// <summary>Asked by clients that want a refresh token; every connection gets one.</summary>
    public const string OfflineAccessScope = "offline_access";

    /// <summary>The longest value of a parameter it reads: the longest redirect_uri, and more than any state needs.</summary>
    public const int MaxParameterLength = AssistantClient.MaxRedirectUriLength;

    /// <summary>
    /// How long a replaced refresh token counts as a renewal that lost a race (an assistant that sent it twice at once)
    /// rather than as a copy. It gets no tokens either way; it only does not end the connection.
    /// </summary>
    public static readonly TimeSpan ConcurrentRenewalWindow = TimeSpan.FromSeconds(10);

    public const string CustomersOnlyMessage =
        "Solo una cuenta de cliente conecta un asistente: inicia sesión con tu cuenta de cliente.";
    public const string UnknownClientMessage =
        "Este asistente no está registrado en TienditApp. Vuelve a agregar el conector desde tu asistente.";
    public const string InvalidRedirectMessage =
        "La dirección a la que regresaría el permiso no es de este asistente. Vuelve a agregar el conector desde tu asistente.";
    public const string InvalidRequestMessage = "La solicitud del asistente no es válida. Vuelve a intentarlo desde tu asistente.";

    /// <summary>A new code or token: 32 random bytes in base64url, 43 characters.</summary>
    public static string NewSecret() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    /// <summary>Whether <paramref name="resource"/> names <paramref name="expected"/>: the same address, with or without a final slash.</summary>
    public static bool IsResource(string? resource, string expected)
        => !string.IsNullOrEmpty(resource)
            && string.Equals(resource.TrimEnd('/'), expected.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The scope a request gets: <see cref="AssistantConnection.AccountScope"/> when it asked for it, for nothing (the
    /// default) or also for <see cref="OfflineAccessScope"/>; null when it asked for anything else.
    /// </summary>
    public static string? ScopeOf(string? scope)
    {
        var asked = (scope ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return asked.All(s => s is AssistantConnection.AccountScope or OfflineAccessScope) ? AssistantConnection.AccountScope : null;
    }

    /// <summary>
    /// The site a redirect_uri goes to, as the permission page shows it: its host, and its port if it is not the default one.
    /// A name with letters of other alphabets is shown as the DNS knows it (punycode, «xn--…»): one that only looks like
    /// another site cannot pass for it.
    /// </summary>
    public static string SiteOf(string redirectUri)
    {
        var uri = new Uri(redirectUri);
        var host = uri.HostNameType == UriHostNameType.Dns ? uri.IdnHost : uri.Host;
        return uri.IsDefaultPort ? host : $"{host}:{uri.Port}";
    }

    /// <summary>Whether a redirect_uri goes to this device (http to localhost): a desktop app, or anything running on it.</summary>
    public static bool IsThisDevice(string redirectUri) => new Uri(redirectUri).Scheme == Uri.UriSchemeHttp;

    /// <summary>
    /// <paramref name="uri"/> with the parameters added to its query, each escaped; the ones without a value are left out.
    /// </summary>
    public static string WithQuery(string uri, params (string Name, string? Value)[] parameters)
    {
        var text = new StringBuilder(uri);
        var separator = !uri.Contains('?') ? "?" : uri.EndsWith('?') || uri.EndsWith('&') ? string.Empty : "&";
        foreach (var (name, value) in parameters)
        {
            if (value is null)
                continue;
            text.Append(separator).Append(Uri.EscapeDataString(name)).Append('=').Append(Uri.EscapeDataString(value));
            separator = "&";
        }
        return text.ToString();
    }
}
