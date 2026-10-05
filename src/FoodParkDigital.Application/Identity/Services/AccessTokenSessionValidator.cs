using System.Security.Claims;
using FoodParkDigital.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Identity.Services;

public interface IAccessTokenSessionValidator
{
    /// <summary>Whether the session of a genuine, unexpired access token is still open.</summary>
    Task<bool> IsOpenAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);

    /// <summary>
    /// The same check for the access tokens of many live connections at once (two queries for all of them): whether
    /// each one's session is still open, in their order.
    /// </summary>
    Task<IReadOnlyList<bool>> AreOpenAsync(IReadOnlyList<ClaimsPrincipal> principals, CancellationToken cancellationToken);
}

/// <summary>
/// An access token counts only while its session is open: the account is active, its security stamp is still the one
/// the token was issued with (a new password, a recovery, or a change of two-step verification or phone changes it)
/// and the session keeps an active refresh token (signing out, a reused token or a new password revoke them). Checked
/// on every authenticated request, so the token stops working then instead of when it expires.
/// </summary>
public sealed class AccessTokenSessionValidator : IAccessTokenSessionValidator
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _clock;

    public AccessTokenSessionValidator(IApplicationDbContext context, IDateTimeProvider clock)
    {
        _context = context;
        _clock = clock;
    }

    public async Task<bool> IsOpenAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (Read(principal) is not (var userId, var sessionId, var stamp))
            return false;

        var now = _clock.UtcNow;
        var account = await _context.Users
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.IsActive,
                u.SecurityStamp,
                SessionOpen = _context.RefreshTokens.Any(t =>
                    t.SessionId == sessionId
                    && t.UserId == userId
                    && t.RevokedAt == null
                    && t.ExpiresAt > now)
            })
            .FirstOrDefaultAsync(cancellationToken);

        return account is { IsActive: true, SessionOpen: true }
            && SessionClaims.StampFingerprint(account.SecurityStamp) == stamp;
    }

    public async Task<IReadOnlyList<bool>> AreOpenAsync(
        IReadOnlyList<ClaimsPrincipal> principals,
        CancellationToken cancellationToken)
    {
        var tokens = principals.Select(Read).ToList();
        var userIds = tokens.OfType<TokenSession>().Select(t => t.UserId).Distinct().ToList();
        var sessionIds = tokens.OfType<TokenSession>().Select(t => t.SessionId).Distinct().ToList();
        if (sessionIds.Count == 0)
            return tokens.Select(_ => false).ToList();

        var now = _clock.UtcNow;
        var accounts = await _context.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.IsActive, u.SecurityStamp })
            .ToDictionaryAsync(u => u.Id, cancellationToken);
        var openSessions = (await _context.RefreshTokens
                .Where(t => sessionIds.Contains(t.SessionId) && t.RevokedAt == null && t.ExpiresAt > now)
                .Select(t => new { t.SessionId, t.UserId })
                .Distinct()
                .ToListAsync(cancellationToken))
            .Select(t => (t.SessionId, t.UserId))
            .ToHashSet();

        return tokens
            .Select(token => token is (var userId, var sessionId, var stamp)
                && openSessions.Contains((sessionId, userId))
                && accounts.TryGetValue(userId, out var account)
                && account.IsActive
                && SessionClaims.StampFingerprint(account.SecurityStamp) == stamp)
            .ToList();
    }

    /// <summary>The session a token carries; none for tokens issued before sessions existed (renewing gives one).</summary>
    private static TokenSession? Read(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId)
        && Guid.TryParse(principal.FindFirst(SessionClaims.SessionId)?.Value, out var sessionId)
        && principal.FindFirst(SessionClaims.SecurityStamp)?.Value is { Length: > 0 } stamp
            ? new TokenSession(userId, sessionId, stamp)
            : null;

    private sealed record TokenSession(Guid UserId, Guid SessionId, string Stamp);
}
