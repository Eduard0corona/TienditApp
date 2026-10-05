using System.Security.Claims;
using FoodParkDigital.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Identity.Services;

public interface IAccessTokenSessionValidator
{
    /// <summary>Whether the session of a genuine, unexpired access token is still open.</summary>
    Task<bool> IsOpenAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
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
        // Tokens issued before sessions existed have neither: renewing them gives one that does.
        if (!Guid.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId)
            || !Guid.TryParse(principal.FindFirst(SessionClaims.SessionId)?.Value, out var sessionId)
            || principal.FindFirst(SessionClaims.SecurityStamp)?.Value is not { Length: > 0 } stamp)
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
}
