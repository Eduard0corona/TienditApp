using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Identity.Services;

/// <summary>
/// The sessions of an account. Each sign-in starts one; its refresh token rotates on every renewal and ending the
/// session revokes all its tokens, which also stops the access tokens that carry it
/// (<see cref="AccessTokenSessionValidator"/>).
/// </summary>
public static class UserSessions
{
    private const int MaxSaveAttempts = 3;

    /// <summary>The stored token whose value the client presented.</summary>
    public static async Task<RefreshToken?> FindAsync(
        IApplicationDbContext context,
        string? value,
        CancellationToken cancellationToken)
    {
        // Blank is never a token.
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var hash = RefreshToken.HashOf(value);
        return await context.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
    }

    /// <summary>Ends a session: its active tokens are revoked and saved, with whatever else the context holds.</summary>
    public static Task EndSessionAsync(
        IApplicationDbContext context,
        Guid sessionId,
        RefreshTokenRevocationReason reason,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        RevokeAsync(context, context.RefreshTokens.Where(t => t.SessionId == sessionId), reason, now, cancellationToken);

    /// <summary>Ends every session of the account and saves.</summary>
    public static Task EndAllAsync(
        IApplicationDbContext context,
        Guid userId,
        RefreshTokenRevocationReason reason,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        RevokeAsync(context, context.RefreshTokens.Where(t => t.UserId == userId), reason, now, cancellationToken);

    private static async Task RevokeAsync(
        IApplicationDbContext context,
        IQueryable<RefreshToken> tokens,
        RefreshTokenRevocationReason reason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var active = await tokens
                .Where(t => t.RevokedAt == null && t.ExpiresAt > now)
                .ToListAsync(cancellationToken);
            foreach (var token in active)
                token.Revoke(reason, now);

            try
            {
                await context.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException ex) when (attempt < MaxSaveAttempts)
            {
                // A renewal replaced one of them meanwhile: read the tokens again, the new one included.
                foreach (var entry in ex.Entries)
                    await entry.ReloadAsync(cancellationToken);
            }
        }
    }
}
