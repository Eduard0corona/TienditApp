using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>A refresh token is kept only as its hash, belongs to a session and stops working once, for one reason.</summary>
public class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NewToken_ShouldKeepOnlyTheHashOfItsValue()
    {
        var token = new RefreshToken(Guid.NewGuid(), Guid.NewGuid(), "valor-del-token", Now, Now.AddDays(7));

        Assert.Equal(RefreshToken.HashOf("valor-del-token"), token.TokenHash);
        Assert.Matches("^[0-9A-F]{64}$", token.TokenHash);
        // Empty, not null: the previous version reads it as required.
        Assert.Equal(string.Empty, token.LegacyValue);
        Assert.True(token.IsActive);
    }

    [Fact]
    public void Token_ShouldBelongToASession_AndHaveAValue()
    {
        Assert.Throws<ArgumentException>(() => new RefreshToken(Guid.NewGuid(), Guid.Empty, "valor", Now, Now.AddDays(7)));
        Assert.Throws<ArgumentException>(() => new RefreshToken(Guid.NewGuid(), Guid.NewGuid(), "", Now, Now.AddDays(7)));
    }

    [Fact]
    public void RevokedToken_ShouldKeepItsFirstReason()
    {
        var token = new RefreshToken(Guid.NewGuid(), Guid.NewGuid(), "valor", Now, Now.AddDays(7));

        token.Revoke(RefreshTokenRevocationReason.Rotated, Now);
        token.Revoke(RefreshTokenRevocationReason.SignedOut, Now.AddMinutes(5));

        Assert.True(token.IsRevoked);
        Assert.True(token.WasRotated);
        Assert.Equal(Now, token.RevokedAt);
    }

    [Theory]
    [InlineData(false, false, null)]
    // Replaced by the previous version: presenting it again still counts as a reuse.
    [InlineData(true, true, RefreshTokenRevocationReason.Rotated)]
    // Signed out with the previous version: no replacement.
    [InlineData(true, false, null)]
    public void LegacyToken_ShouldKeepOnlyItsHash(bool revoked, bool replaced, RefreshTokenRevocationReason? expectedReason)
    {
        var token = Legacy("valor-anterior", revoked ? Now : null, replaced ? "valor-siguiente" : null);

        token.HashLegacyValue();

        Assert.Equal(RefreshToken.HashOf("valor-anterior"), token.TokenHash);
        Assert.Equal(string.Empty, token.LegacyValue);
        Assert.Null(token.LegacyReplacementValue);
        Assert.Equal(expectedReason, token.RevocationReason);
    }

    [Fact]
    public void TokenLeftWithoutItsHashByARollback_ShouldNotGetTheHashOfAnEmptyValue()
    {
        // Hashed by this version, then the rollback dropped the hash and kept the empty value: it opens nothing.
        var token = Legacy("", null, null);

        token.HashLegacyValue();

        Assert.Null(token.TokenHash);
        Assert.Equal(string.Empty, token.LegacyValue);
    }

    [Fact]
    public void RevocationReasons_ShouldKeepTheirStoredNumbers()
    {
        Assert.Equal(
            [1, 2, 3, 4, 5],
            Enum.GetValues<RefreshTokenRevocationReason>().Select(reason => (int)reason));
        Assert.Equal(3, (int)RefreshTokenRevocationReason.ReuseDetected);
    }

    /// <summary>A row the previous version wrote: the plain value and no hash.</summary>
    private static RefreshToken Legacy(string value, DateTimeOffset? revokedAt, string? replacement)
    {
        var token = new RefreshToken(Guid.NewGuid(), Guid.NewGuid(), "otro-valor", Now, Now.AddDays(7));
        Set(token, nameof(RefreshToken.TokenHash), null);
        Set(token, nameof(RefreshToken.LegacyValue), value);
        Set(token, nameof(RefreshToken.LegacyReplacementValue), replacement);
        Set(token, nameof(RefreshToken.RevokedAt), revokedAt);
        return token;
    }

    private static void Set(RefreshToken token, string property, object? value) =>
        typeof(RefreshToken).GetProperty(property)!.SetValue(token, value);
}
