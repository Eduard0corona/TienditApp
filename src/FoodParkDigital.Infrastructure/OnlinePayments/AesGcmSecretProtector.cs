using System.Security.Cryptography;
using System.Text;
using FoodParkDigital.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;

namespace FoodParkDigital.Infrastructure.OnlinePayments;

/// <summary>
/// AES-256-GCM with the key of <c>SecretProtection:Key</c> (32 bytes in base64), which lives in the configuration (an App
/// Service setting or Key Vault), never next to the data. Each value carries a fresh nonce and is authenticated with its
/// purpose, so a value copied to another business's row fails to decrypt instead of being used there. The prefix names the
/// format, for a future change of key.
/// </summary>
public sealed class AesGcmSecretProtector : ISecretProtector
{
    public const string KeySetting = "SecretProtection:Key";

    private const string Prefix = "v1.";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[]? _key;

    public AesGcmSecretProtector(IConfiguration configuration)
    {
        var configured = configuration[KeySetting];
        if (string.IsNullOrWhiteSpace(configured))
            return;

        try
        {
            var key = Convert.FromBase64String(configured.Trim());
            _key = key.Length == 32 ? key : null;
        }
        catch (FormatException)
        {
            _key = null;
        }
    }

    public bool IsConfigured => _key is not null;

    public string Protect(string plaintext, string purpose)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        var key = _key ?? throw new InvalidOperationException($"{KeySetting} no está configurada.");

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var data = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[data.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(key, TagSize))
            aes.Encrypt(nonce, data, cipher, tag, Encoding.UTF8.GetBytes(purpose));

        var payload = new byte[NonceSize + cipher.Length + TagSize];
        nonce.CopyTo(payload, 0);
        cipher.CopyTo(payload, NonceSize);
        tag.CopyTo(payload, NonceSize + cipher.Length);
        return Prefix + Convert.ToBase64String(payload);
    }

    public string Unprotect(string protectedText, string purpose)
    {
        ArgumentNullException.ThrowIfNull(protectedText);
        var key = _key ?? throw new InvalidOperationException($"{KeySetting} no está configurada.");
        if (!protectedText.StartsWith(Prefix, StringComparison.Ordinal))
            throw new CryptographicException("The value was not protected by this protector.");

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(protectedText[Prefix.Length..]);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("The protected value is malformed.", ex);
        }

        if (payload.Length < NonceSize + TagSize)
            throw new CryptographicException("The protected value is malformed.");

        var nonce = payload.AsSpan(0, NonceSize);
        var cipher = payload.AsSpan(NonceSize, payload.Length - NonceSize - TagSize);
        var tag = payload.AsSpan(payload.Length - TagSize);
        var data = new byte[cipher.Length];
        using (var aes = new AesGcm(key, TagSize))
            aes.Decrypt(nonce, cipher, tag, data, Encoding.UTF8.GetBytes(purpose));
        return Encoding.UTF8.GetString(data);
    }
}
