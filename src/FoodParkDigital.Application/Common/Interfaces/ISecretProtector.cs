namespace FoodParkDigital.Application.Common.Interfaces;

/// <summary>
/// Encrypts the third-party keys TienditApp keeps on behalf of a business (Fase 24I: its Mercado Pago account), with a
/// key from the configuration and never from the database. Each value is bound to its <paramref name="purpose"/> (for
/// instance, the business it belongs to), so a value copied to another row does not decrypt there.
/// </summary>
public interface ISecretProtector
{
    /// <summary>Whether the encryption key is configured.</summary>
    bool IsConfigured { get; }

    string Protect(string plaintext, string purpose);

    /// <exception cref="System.Security.Cryptography.CryptographicException">The value was not encrypted for this purpose with this key.</exception>
    string Unprotect(string protectedText, string purpose);
}
