using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Assistants;
using FoodParkDigital.Domain.Assistants.Entities;
using FoodParkDigital.Domain.Assistants.Enums;
using FoodParkDigital.Domain.Identity.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.Assistants;

/// <summary>A request to the token endpoint (RFC 6749, 4.1.3 and 6), with RFC 8707's resource.</summary>
[AllowAnonymousRequest("Un asistente de IA cambia su código o su token de actualización por tokens (OAuth): sin ellos no hay nada.")]
public record ExchangeAssistantTokenCommand : IRequest<Result<AssistantTokensDto>>, ITransactionalRequest
{
    public string? GrantType { get; init; }
    public string? Code { get; init; }
    public string? RedirectUri { get; init; }
    public string? ClientId { get; init; }
    public string? CodeVerifier { get; init; }
    public string? RefreshToken { get; init; }
    public string? Resource { get; init; }
}

/// <summary>
/// The tokens the assistant gets, or the OAuth error that says why not. A refused exchange is still a handled one: what it
/// stopped (a connection whose code or token was copied) is saved.
/// </summary>
public sealed class AssistantTokensDto
{
    public string? AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public int ExpiresIn { get; init; }
    public string? Scope { get; init; }
    public string? Error { get; init; }
    public string? ErrorDescription { get; init; }

    public static AssistantTokensDto Refused(string error, string description) => new() { Error = error, ErrorDescription = description };
}

/// <summary>
/// Gives an assistant its tokens (Fase 32E, ADR 0097): for a code, once, with its PKCE verifier and everything it was tied
/// to, and then a new connection of the account (replacing the one it had with the same assistant); for a refresh token,
/// the next one and a new access token. A code or a replaced refresh token presented again was copied: its connection ends.
/// Two exchanges of the same code or connection at once wait for each other.
/// </summary>
public class ExchangeAssistantTokenCommandHandler : IRequestHandler<ExchangeAssistantTokenCommand, Result<AssistantTokensDto>>
{
    public const string InvalidCodeMessage = "El código no es válido, ya se usó o venció.";
    public const string InvalidRefreshMessage = "El token de actualización no es válido, ya se usó o venció.";
    public const string ConcurrentRenewalMessage = "El token se acaba de cambiar en otra solicitud: usa el nuevo.";

    private readonly IApplicationDbContext _context;
    private readonly IAssistantAuthorizationLock _lock;
    private readonly IAssistantAccountServer _server;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<ExchangeAssistantTokenCommandHandler> _logger;

    public ExchangeAssistantTokenCommandHandler(
        IApplicationDbContext context,
        IAssistantAuthorizationLock authorizationLock,
        IAssistantAccountServer server,
        IDateTimeProvider clock,
        ILogger<ExchangeAssistantTokenCommandHandler> logger)
    {
        _context = context;
        _lock = authorizationLock;
        _server = server;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<AssistantTokensDto>> Handle(ExchangeAssistantTokenCommand request, CancellationToken cancellationToken)
    {
        if (request.Resource is not null && !AssistantAuthorization.IsResource(request.Resource, _server.Resource))
            return Result.Success(AssistantTokensDto.Refused(OAuthErrors.InvalidTarget, $"El recurso es {_server.Resource}."));

        return request.GrantType switch
        {
            AssistantAuthorization.AuthorizationCodeGrant => Result.Success(await ExchangeCodeAsync(request, cancellationToken)),
            AssistantAuthorization.RefreshTokenGrant => Result.Success(await RenewAsync(request, cancellationToken)),
            _ => Result.Success(AssistantTokensDto.Refused(OAuthErrors.UnsupportedGrantType, "Solo authorization_code y refresh_token."))
        };
    }

    private async Task<AssistantTokensDto> ExchangeCodeAsync(ExchangeAssistantTokenCommand request, CancellationToken cancellationToken)
    {
        if (!AssistantSecrets.IsWellFormed(request.Code) || string.IsNullOrEmpty(request.RedirectUri)
            || !AssistantSecrets.IsWellFormed(request.ClientId))
            return AssistantTokensDto.Refused(OAuthErrors.InvalidRequest, "Faltan code, redirect_uri o client_id.");

        var hash = AssistantSecrets.HashOf(request.Code!);
        var codeId = await _context.AssistantAuthorizationCodes.Where(c => c.CodeHash == hash).Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (codeId is null)
            return AssistantTokensDto.Refused(OAuthErrors.InvalidGrant, InvalidCodeMessage);

        // Read again once its lock is held: a second exchange at once finds it redeemed.
        await _lock.AcquireAsync(codeId.Value, cancellationToken);
        var code = await _context.AssistantAuthorizationCodes.SingleAsync(c => c.Id == codeId, cancellationToken);
        var now = _clock.UtcNow;
        if (code.RedeemedAt is not null)
        {
            // Someone else has the code: whatever it gave stops (RFC 6749, 4.1.2).
            if (await _context.AssistantConnections.FirstOrDefaultAsync(c => c.Id == code.ConnectionId, cancellationToken) is { } given)
            {
                given.Revoke(now);
                await _context.SaveChangesAsync(cancellationToken);
                _logger.LogWarning("An assistant's authorization code was presented again: connection {ConnectionId} ended.", given.Id);
            }
            return AssistantTokensDto.Refused(OAuthErrors.InvalidGrant, InvalidCodeMessage);
        }

        var client = await _context.AssistantClients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == code.ClientId, cancellationToken);
        if (code.IsExpired(now) || client is null || client.ClientId != request.ClientId
            || code.RedirectUri != request.RedirectUri || !code.Verifies(request.CodeVerifier))
            return AssistantTokensDto.Refused(OAuthErrors.InvalidGrant, InvalidCodeMessage);
        if (!await IsActiveCustomerAsync(code.UserId, cancellationToken))
            return AssistantTokensDto.Refused(OAuthErrors.InvalidGrant, InvalidCodeMessage);

        // Allowing the same assistant again replaces its connection; the database keeps one working per account and assistant.
        var previous = await _context.AssistantConnections
            .Where(c => c.UserId == code.UserId && c.ClientId == code.ClientId && c.RevokedAt == null)
            .ToListAsync(cancellationToken);
        previous.ForEach(c => c.Revoke(now));
        if (previous.Count > 0)
            await _context.SaveChangesAsync(cancellationToken);

        var connection = AssistantConnection.Grant(code.UserId, code.ClientId, code.Scope, now);
        _context.AssistantConnections.Add(connection);
        code.Redeem(connection.Id, now);
        var tokens = Issue(connection, now);
        await _context.SaveChangesAsync(cancellationToken);
        return tokens;
    }

    private async Task<AssistantTokensDto> RenewAsync(ExchangeAssistantTokenCommand request, CancellationToken cancellationToken)
    {
        if (!AssistantSecrets.IsWellFormed(request.RefreshToken))
            return AssistantTokensDto.Refused(OAuthErrors.InvalidRequest, "Falta refresh_token.");

        var hash = AssistantSecrets.HashOf(request.RefreshToken!);
        var found = await _context.AssistantTokens
            .Where(t => t.TokenHash == hash && t.Kind == AssistantTokenKind.Refresh)
            .Select(t => new { t.Id, t.ConnectionId })
            .FirstOrDefaultAsync(cancellationToken);
        if (found is null)
            return AssistantTokensDto.Refused(OAuthErrors.InvalidGrant, InvalidRefreshMessage);

        // Read again once the connection's lock is held: a second renewal at once finds it replaced.
        await _lock.AcquireAsync(found.ConnectionId, cancellationToken);
        var token = await _context.AssistantTokens.SingleAsync(t => t.Id == found.Id, cancellationToken);
        var connection = await _context.AssistantConnections.SingleAsync(c => c.Id == found.ConnectionId, cancellationToken);
        var client = await _context.AssistantClients.AsNoTracking().SingleAsync(c => c.Id == connection.ClientId, cancellationToken);
        var now = _clock.UtcNow;
        if (!connection.IsActive || (request.ClientId is not null && request.ClientId != client.ClientId))
            return AssistantTokensDto.Refused(OAuthErrors.InvalidGrant, InvalidRefreshMessage);

        if (token.RotatedAt is { } rotated)
        {
            // Its twin request replaced it a moment ago: this one gets nothing, and the assistant keeps the new one.
            if (rotated > now - AssistantAuthorization.ConcurrentRenewalWindow)
                return AssistantTokensDto.Refused(OAuthErrors.InvalidGrant, ConcurrentRenewalMessage);

            connection.Revoke(now);
            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogWarning(
                "A replaced refresh token of an assistant was presented again: connection {ConnectionId} of user {UserId} ended.",
                connection.Id, connection.UserId);
            return AssistantTokensDto.Refused(OAuthErrors.InvalidGrant, InvalidRefreshMessage);
        }

        if (token.IsExpired(now) || !await IsActiveCustomerAsync(connection.UserId, cancellationToken))
            return AssistantTokensDto.Refused(OAuthErrors.InvalidGrant, InvalidRefreshMessage);

        token.Rotate(now);
        connection.RecordUse(now);
        var tokens = Issue(connection, now);
        await _context.SaveChangesAsync(cancellationToken);
        return tokens;
    }

    private AssistantTokensDto Issue(AssistantConnection connection, DateTimeOffset now)
    {
        var access = AssistantAuthorization.NewSecret();
        var refresh = AssistantAuthorization.NewSecret();
        _context.AssistantTokens.Add(AssistantToken.Issue(connection.Id, AssistantTokenKind.Access, access, now));
        _context.AssistantTokens.Add(AssistantToken.Issue(connection.Id, AssistantTokenKind.Refresh, refresh, now));
        return new AssistantTokensDto
        {
            AccessToken = access,
            RefreshToken = refresh,
            ExpiresIn = (int)AssistantToken.AccessLifetime.TotalSeconds,
            Scope = connection.Scope
        };
    }

    private Task<bool> IsActiveCustomerAsync(Guid userId, CancellationToken cancellationToken)
        => _context.Users.AnyAsync(u => u.Id == userId && u.IsActive && u.Role == UserRole.Customer, cancellationToken);
}

/// <summary>An assistant gives up a token (RFC 7009): its connection ends with it.</summary>
[AllowAnonymousRequest("Un asistente de IA revoca su token (OAuth, RFC 7009): solo detiene su conexión.")]
public record RevokeAssistantTokenCommand(string? Token, string? ClientId) : IRequest<Result>, ITransactionalRequest;

/// <summary>
/// Ends the connection of the token given, access or refresh, if it is one and of this client. It answers the same whether
/// it was or not, as RFC 7009 asks: nobody learns from it which tokens exist.
/// </summary>
public class RevokeAssistantTokenCommandHandler : IRequestHandler<RevokeAssistantTokenCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _clock;

    public RevokeAssistantTokenCommandHandler(IApplicationDbContext context, IDateTimeProvider clock)
    {
        _context = context;
        _clock = clock;
    }

    public async Task<Result> Handle(RevokeAssistantTokenCommand request, CancellationToken cancellationToken)
    {
        if (!AssistantSecrets.IsWellFormed(request.Token))
            return Result.Success();

        var hash = AssistantSecrets.HashOf(request.Token!);
        var connection = await (
                from token in _context.AssistantTokens
                where token.TokenHash == hash
                join given in _context.AssistantConnections on token.ConnectionId equals given.Id
                join client in _context.AssistantClients on given.ClientId equals client.Id
                where request.ClientId == null || client.ClientId == request.ClientId
                select given)
            .FirstOrDefaultAsync(cancellationToken);
        if (connection is null || !connection.IsActive)
            return Result.Success();

        connection.Revoke(_clock.UtcNow);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>Who an access token of /mcp/cuenta is: the customer account and the connection it came from.</summary>
public sealed record AssistantIdentityDto(Guid UserId, string? Email, Guid ConnectionId);

/// <summary>
/// The identity behind an access token of /mcp/cuenta (Fase 32E, ADR 0097), on every request: the token in its hour, its
/// connection not disconnected and its account an active customer one. Writes when the connection was last used, at most
/// every five minutes.
/// </summary>
[AllowAnonymousRequest("La identidad de un asistente de IA por el token de acceso que le dio una cuenta: sin un token válido no hay ninguna.")]
public record AuthenticateAssistantCommand(string? AccessToken) : IRequest<AssistantIdentityDto?>;

public class AuthenticateAssistantCommandHandler : IRequestHandler<AuthenticateAssistantCommand, AssistantIdentityDto?>
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _clock;

    public AuthenticateAssistantCommandHandler(IApplicationDbContext context, IDateTimeProvider clock)
    {
        _context = context;
        _clock = clock;
    }

    public async Task<AssistantIdentityDto?> Handle(AuthenticateAssistantCommand request, CancellationToken cancellationToken)
    {
        if (!AssistantSecrets.IsWellFormed(request.AccessToken))
            return null;

        var hash = AssistantSecrets.HashOf(request.AccessToken!);
        var now = _clock.UtcNow;
        var found = await (
                from token in _context.AssistantTokens
                where token.TokenHash == hash && token.Kind == AssistantTokenKind.Access && token.ExpiresAt > now
                join connection in _context.AssistantConnections on token.ConnectionId equals connection.Id
                where connection.RevokedAt == null
                join user in _context.Users on connection.UserId equals user.Id
                where user.IsActive && user.Role == UserRole.Customer
                select new { Connection = connection, user.Email })
            .FirstOrDefaultAsync(cancellationToken);
        if (found is null)
            return null;

        if (found.Connection.RecordUse(now))
            await _context.SaveChangesAsync(cancellationToken);
        return new AssistantIdentityDto(found.Connection.UserId, found.Email, found.Connection.Id);
    }
}
