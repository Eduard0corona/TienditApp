using System.Security.Cryptography;
using FoodParkDigital.Infrastructure.OnlinePayments;
using Microsoft.Extensions.Configuration;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// The keys of a business's Mercado Pago account are stored encrypted (Fase 24I, ADR 0031): only with the configured key
/// and for the same purpose (the business) they decrypt; anything else fails instead of being used.
/// </summary>
public class AesGcmSecretProtectorTests
{
    private const string Key = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";
    private const string OtherKey = "HxwdHhkaGxwVFhcYERITFA0ODxAJCgsMBQYHCAECAwQ=";

    private static AesGcmSecretProtector Create(string? key) =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [AesGcmSecretProtector.KeySetting] = key })
            .Build());

    [Fact]
    public void Protect_ShouldGiveBackTheValue_OnlyForTheSamePurpose()
    {
        var protector = Create(Key);

        var stored = protector.Protect("APP_USR-1234-access", "tenant-a");

        Assert.StartsWith("v1.", stored);
        Assert.DoesNotContain("APP_USR", stored);
        Assert.Equal("APP_USR-1234-access", protector.Unprotect(stored, "tenant-a"));
        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(stored, "tenant-b"));
    }

    [Fact]
    public void Protect_ShouldNeverRepeatTheStoredValue()
    {
        var protector = Create(Key);

        Assert.NotEqual(protector.Protect("same", "tenant-a"), protector.Protect("same", "tenant-a"));
    }

    [Fact]
    public void Unprotect_ShouldFail_WithAnotherKeyOrAChangedValue()
    {
        var stored = Create(Key).Protect("APP_USR-1234-access", "tenant-a");
        var payload = Convert.FromBase64String(stored[3..]);
        payload[^1] ^= 0x01;
        var changed = "v1." + Convert.ToBase64String(payload);

        Assert.ThrowsAny<CryptographicException>(() => Create(OtherKey).Unprotect(stored, "tenant-a"));
        Assert.ThrowsAny<CryptographicException>(() => Create(Key).Unprotect(changed, "tenant-a"));
        Assert.ThrowsAny<CryptographicException>(() => Create(Key).Unprotect("APP_USR-1234-access", "tenant-a"));
        Assert.ThrowsAny<CryptographicException>(() => Create(Key).Unprotect("v1.not-base64!", "tenant-a"));
        Assert.ThrowsAny<CryptographicException>(() => Create(Key).Unprotect("v1.AAAA", "tenant-a"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64")]
    [InlineData("AAECAwQFBgcICQoLDA0ODw==")]
    public void Protector_ShouldRefuseToWork_WithoutA256BitKey(string? key)
    {
        var protector = Create(key);

        Assert.False(protector.IsConfigured);
        Assert.Throws<InvalidOperationException>(() => protector.Protect("value", "tenant-a"));
        Assert.Throws<InvalidOperationException>(() => protector.Unprotect("v1.AAAA", "tenant-a"));
    }
}
