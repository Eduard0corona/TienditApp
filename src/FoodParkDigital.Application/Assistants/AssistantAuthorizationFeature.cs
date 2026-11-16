using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Domain.Assistants;
using FoodParkDigital.Domain.Assistants.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Assistants;

/// <summary>What an assistant's application sends to register (RFC 7591): only public clients of the code flow.</summary>
[AllowAnonymousRequest("Registro dinámico de un asistente de IA (OAuth, RFC 7591): no lee nada hasta que una cuenta le da permiso.")]
public record RegisterAssistantClientCommand : IRequest<RegisteredAssistantClientDto>
{
    public string? ClientName { get; init; }
    public List<string>? RedirectUris { get; init; }
    public string? TokenEndpointAuthMethod { get; init; }
    public List<string>? GrantTypes { get; init; }
    public List<string>? ResponseTypes { get; init; }
}

/// <summary>The registered client, or the OAuth error that says why it was not.</summary>
public sealed class RegisteredAssistantClientDto
{
    public string? ClientId { get; init; }
    public string? ClientName { get; init; }
    public IReadOnlyList<string> RedirectUris { get; init; } = [];
    public DateTimeOffset IssuedAt { get; init; }
    public string? Error { get; init; }
    public string? ErrorDescription { get; init; }

    public static RegisteredAssistantClientDto Refused(string error, string description) => new() { Error = error, ErrorDescription = description };
}

/// <summary>
/// Registers a public client (Fase 32E, ADR 0097). Anyone may: the table is bounded by
/// <see cref="AssistantClient.MaxRegisteredPerHour"/> from every address, by what one client may write, and by the cleanup
/// of those no account connected in a day.
/// </summary>
public class RegisterAssistantClientCommandHandler : IRequestHandler<RegisterAssistantClientCommand, RegisteredAssistantClientDto>
{
    public const string FullMessage = "Hay demasiados registros de asistentes en este momento: intenta en una hora.";

    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _clock;

    public RegisterAssistantClientCommandHandler(IApplicationDbContext context, IDateTimeProvider clock)
    {
        _context = context;
        _clock = clock;
    }

    public async Task<RegisteredAssistantClientDto> Handle(RegisterAssistantClientCommand request, CancellationToken cancellationToken)
    {
        if (request.TokenEndpointAuthMethod is { } method && method != AssistantAuthorization.ClientAuthenticationMethod)
            return RegisteredAssistantClientDto.Refused(OAuthErrors.InvalidClientMetadata, "Solo se registran clientes públicos (token_endpoint_auth_method none).");
        if (request.GrantTypes is { } grants
            && grants.Any(g => g is not (AssistantAuthorization.AuthorizationCodeGrant or AssistantAuthorization.RefreshTokenGrant)))
            return RegisteredAssistantClientDto.Refused(OAuthErrors.InvalidClientMetadata, "Solo authorization_code y refresh_token.");
        if (request.ResponseTypes is { } responses && responses.Any(r => r != AssistantAuthorization.ResponseType))
            return RegisteredAssistantClientDto.Refused(OAuthErrors.InvalidClientMetadata, "Solo response_type code.");
        if (request.ClientName is { Length: > AssistantAuthorization.MaxParameterLength })
            return RegisteredAssistantClientDto.Refused(OAuthErrors.InvalidClientMetadata, "El nombre es demasiado largo.");

        var uris = request.RedirectUris ?? [];
        if (uris.Count == 0 || uris.Count > AssistantClient.MaxRedirectUris || !uris.All(AssistantClient.IsRedirectUri))
            return RegisteredAssistantClientDto.Refused(OAuthErrors.InvalidRedirectUri,
                $"De 1 a {AssistantClient.MaxRedirectUris} direcciones de regreso: https, o http a localhost, sin fragmento, de hasta {AssistantClient.MaxRedirectUriLength} caracteres.");

        var now = _clock.UtcNow;
        var lastHour = now.AddHours(-1);
        if (await _context.AssistantClients.CountAsync(c => c.RegisteredAt > lastHour, cancellationToken) >= AssistantClient.MaxRegisteredPerHour)
            return RegisteredAssistantClientDto.Refused(OAuthErrors.TemporarilyUnavailable, FullMessage);

        var client = AssistantClient.Register(AssistantAuthorization.NewSecret(), request.ClientName, uris, now);
        _context.AssistantClients.Add(client);
        await _context.SaveChangesAsync(cancellationToken);
        return new RegisteredAssistantClientDto
        {
            ClientId = client.ClientId,
            ClientName = client.Name,
            RedirectUris = client.RedirectUris,
            IssuedAt = client.RegisteredAt
        };
    }
}

/// <summary>
/// The parameters of an authorization request (RFC 6749, 4.1.1, with PKCE and RFC 8707's resource), as the assistant sent
/// them to /oauth/authorize and the permission page sends them back.
/// </summary>
public abstract record AssistantAuthorizationRequest
{
    public string? ResponseType { get; init; }
    public string? ClientId { get; init; }
    public string? RedirectUri { get; init; }
    public string? CodeChallenge { get; init; }
    public string? CodeChallengeMethod { get; init; }
    public string? Resource { get; init; }
    public string? Scope { get; init; }
    public string? State { get; init; }
}

/// <summary>Whether an authorization request may ask the customer, and what the permission page shows of it.</summary>
public sealed class AssistantAuthorizationCheckDto
{
    /// <summary>Everything is in order: the permission page may ask.</summary>
    public bool Valid { get; init; }

    /// <summary>
    /// What is wrong, when it is the client or its address: the page says it and never sends the browser anywhere, as
    /// RFC 6749 asks (it could be someone else's site).
    /// </summary>
    public string? Problem { get; init; }

    /// <summary>Where to return with the OAuth error, when the client and its address are right but something else is not.</summary>
    public string? ErrorRedirectUrl { get; init; }

    /// <summary>The name the assistant gave when it registered; null when it gave none.</summary>
    public string? ClientName { get; init; }

    /// <summary>The site the permission returns to (the host of its redirect_uri): the one thing the customer can check.</summary>
    public string? RedirectSite { get; init; }

    /// <summary>It returns to this device (http to localhost): a desktop app, or anything else running on it.</summary>
    public bool RedirectsToThisDevice { get; init; }
}

/// <summary>
/// Checks an authorization request without asking anything: the permission page starts here (GET /oauth/authorize only sends
/// the browser to it, unchecked).
/// </summary>
[AllowAnonymousRequest("Revisa una solicitud de permiso de un asistente de IA (OAuth) antes de pedirlo: no da nada.")]
public record CheckAssistantAuthorizationQuery : AssistantAuthorizationRequest, IRequest<AssistantAuthorizationCheckDto>;

public class CheckAssistantAuthorizationQueryHandler : IRequestHandler<CheckAssistantAuthorizationQuery, AssistantAuthorizationCheckDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IAssistantAccountServer _server;

    public CheckAssistantAuthorizationQueryHandler(IApplicationDbContext context, IAssistantAccountServer server)
    {
        _context = context;
        _server = server;
    }

    public async Task<AssistantAuthorizationCheckDto> Handle(CheckAssistantAuthorizationQuery request, CancellationToken cancellationToken)
        => (await AssistantAuthorizationRequests.CheckAsync(_context, request, _server.Resource, cancellationToken)).Check;
}

/// <summary>Where the browser goes after the customer answered: the assistant, with its code or with access_denied.</summary>
public sealed record AssistantAuthorizationRedirectDto(string RedirectUrl);

/// <summary>
/// The customer allows the assistant, or does not (Fase 32E, ADR 0097), from the permission page with their session: the
/// request is checked again, and allowing it gives a code of one use for this account; not allowing it returns
/// access_denied. Only a customer account: a business's team has permissions an assistant must not have.
/// </summary>
[RequiresAuthentication]
public record ApproveAssistantAuthorizationCommand : AssistantAuthorizationRequest,
    IRequest<Result<AssistantAuthorizationRedirectDto>>, ITransactionalRequest
{
    public bool Allow { get; init; }
}

public class ApproveAssistantAuthorizationCommandHandler
    : IRequestHandler<ApproveAssistantAuthorizationCommand, Result<AssistantAuthorizationRedirectDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IAssistantAccountServer _server;
    private readonly IDateTimeProvider _clock;

    public ApproveAssistantAuthorizationCommandHandler(
        IApplicationDbContext context, ICurrentUserService currentUser, IAssistantAccountServer server, IDateTimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _server = server;
        _clock = clock;
    }

    public async Task<Result<AssistantAuthorizationRedirectDto>> Handle(
        ApproveAssistantAuthorizationCommand request, CancellationToken cancellationToken)
    {
        var account = await CustomerAccounts.FindSignedInAsync(_context, _currentUser, cancellationToken);
        if (account is null)
            return Result.Failure<AssistantAuthorizationRedirectDto>(AssistantAuthorization.CustomersOnlyMessage);

        var (check, client, scope) = await AssistantAuthorizationRequests.CheckAsync(_context, request, _server.Resource, cancellationToken);
        if (check.Problem is not null)
            return Result.Failure<AssistantAuthorizationRedirectDto>(check.Problem);
        if (check.ErrorRedirectUrl is not null)
            return Result.Success(new AssistantAuthorizationRedirectDto(check.ErrorRedirectUrl));
        if (!request.Allow)
            return Result.Success(new AssistantAuthorizationRedirectDto(AssistantAuthorization.WithQuery(request.RedirectUri!,
                ("error", OAuthErrors.AccessDenied), ("error_description", "El cliente no dio permiso."), ("state", request.State))));

        // Allowing the same assistant again replaces its connection: it does not count.
        var connected = await _context.AssistantConnections.CountAsync(
            c => c.UserId == account.Id && c.RevokedAt == null && c.ClientId != client!.Id, cancellationToken);
        if (connected >= AssistantConnection.MaxActivePerAccount)
            return Result.Failure<AssistantAuthorizationRedirectDto>(AssistantConnection.TooManyMessage);

        var code = AssistantAuthorization.NewSecret();
        _context.AssistantAuthorizationCodes.Add(AssistantAuthorizationCode.Issue(
            client!, account.Id, code, request.RedirectUri!, request.CodeChallenge!, _server.Resource, scope!, _clock.UtcNow));
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(new AssistantAuthorizationRedirectDto(
            AssistantAuthorization.WithQuery(request.RedirectUri!, ("code", code), ("state", request.State))));
    }
}

/// <summary>The check of an authorization request, shared by the permission page's check and its answer.</summary>
internal static class AssistantAuthorizationRequests
{
    public static async Task<(AssistantAuthorizationCheckDto Check, AssistantClient? Client, string? Scope)> CheckAsync(
        IApplicationDbContext context, AssistantAuthorizationRequest request, string resource, CancellationToken cancellationToken)
    {
        // First the client and its address, said on the page: an error never goes to an address that is not the client's.
        var client = AssistantSecrets.IsWellFormed(request.ClientId)
            ? await context.AssistantClients.AsNoTracking().FirstOrDefaultAsync(c => c.ClientId == request.ClientId, cancellationToken)
            : null;
        if (client is null)
            return (new AssistantAuthorizationCheckDto { Problem = AssistantAuthorization.UnknownClientMessage }, null, null);
        if (!client.AllowsRedirect(request.RedirectUri))
            return (new AssistantAuthorizationCheckDto { Problem = AssistantAuthorization.InvalidRedirectMessage, ClientName = client.Name }, client, null);

        var redirect = request.RedirectUri!;
        var scope = AssistantAuthorization.ScopeOf(request.Scope);
        var (error, description) =
            request.ResponseType != AssistantAuthorization.ResponseType
                ? (OAuthErrors.UnsupportedResponseType, "Solo response_type code.")
            : request.CodeChallengeMethod != AssistantAuthorization.ChallengeMethod || !AssistantSecrets.IsCodeChallenge(request.CodeChallenge)
                ? (OAuthErrors.InvalidRequest, "PKCE es obligatorio: code_challenge con code_challenge_method S256.")
            : scope is null
                ? (OAuthErrors.InvalidScope, $"El único scope es {AssistantConnection.AccountScope}.")
            : request.Resource is not null && !AssistantAuthorization.IsResource(request.Resource, resource)
                ? (OAuthErrors.InvalidTarget, $"El recurso es {resource}.")
            : request.State is { Length: > AssistantAuthorization.MaxParameterLength }
                ? (OAuthErrors.InvalidRequest, "El state es demasiado largo.")
            : ((string?)null, (string?)null);

        var check = new AssistantAuthorizationCheckDto
        {
            Valid = error is null,
            ErrorRedirectUrl = error is null
                ? null
                : AssistantAuthorization.WithQuery(redirect, ("error", error), ("error_description", description),
                    ("state", request.State is { Length: > AssistantAuthorization.MaxParameterLength } ? null : request.State)),
            ClientName = client.Name,
            RedirectSite = AssistantAuthorization.SiteOf(redirect),
            RedirectsToThisDevice = AssistantAuthorization.IsThisDevice(redirect)
        };
        return (check, client, scope);
    }
}
