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

    [Fact]
    public void Hash_ShouldBeTheUppercaseHexOfTheSha256OfTheUtf8Value()
    {
        // The migration StopUsingPlainRefreshTokenValues computes the same in SQL (HASHBYTES, style 2) for the plain
        // values older versions stored: a change here would end those sessions.
        Assert.Equal(
            "9F86D081884C7D659A2FEAA0C55AD015A3BF4F1B2B0B822CD15D6C15B0F00A08",
            RefreshToken.HashOf("test"));
    }

    [Fact]
    public void RevocationReasons_ShouldKeepTheirStoredNumbers()
    {
        Assert.Equal(
            [1, 2, 3, 4, 5],
            Enum.GetValues<RefreshTokenRevocationReason>().Select(reason => (int)reason));
        Assert.Equal(3, (int)RefreshTokenRevocationReason.ReuseDetected);
    }
}
