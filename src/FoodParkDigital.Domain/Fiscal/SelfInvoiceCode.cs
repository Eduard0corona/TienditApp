using System.Security.Cryptography;
using System.Text;

namespace FoodParkDigital.Domain.Fiscal;

/// <summary>
/// The code of a ticket for self-invoicing (Fase 28F, ADR 0050): 10 characters of Crockford's base32 (50 bits) out of an
/// HMAC-SHA256 of the order's id with the business's key. It is never stored: it is computed for the ticket and compared
/// again, in constant time, when the customer types it. Without the key, the folio printed on the ticket tells nothing.
/// </summary>
public static class SelfInvoiceCode
{
    public const int Length = 10;
    public const int KeyLength = 32;

    // Crockford's base32: no I, L, O or U, so what is read off a ticket is typed back.
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>A new key for a business: 32 random bytes.</summary>
    public static byte[] NewKey() => RandomNumberGenerator.GetBytes(KeyLength);

    /// <summary>The code of the order, without the dash.</summary>
    public static string For(byte[] key, Guid orderId)
    {
        ArgumentNullException.ThrowIfNull(key);
        Span<byte> hash = stackalloc byte[32];
        HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(orderId.ToString("N")), hash);
        // The first 50 bits, five at a time.
        var code = new char[Length];
        ulong bits = 0;
        for (var i = 0; i < 7; i++)
            bits = (bits << 8) | hash[i];
        bits >>= 6;
        for (var i = Length - 1; i >= 0; i--)
        {
            code[i] = Alphabet[(int)(bits & 31)];
            bits >>= 5;
        }
        return new string(code);
    }

    /// <summary>As the ticket shows it: «ABCDE-12345».</summary>
    public static string Format(string code) => code.Length == Length ? $"{code[..5]}-{code[5..]}" : code;

    /// <summary>
    /// What the customer typed, as a code: without spaces or dashes, in capitals, and with what is easy to misread (O, I,
    /// L) read as Crockford reads it; null when it cannot be one.
    /// </summary>
    public static string? Normalize(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed))
            return null;
        var code = new StringBuilder(Length);
        foreach (var c in typed)
        {
            if (c is ' ' or '-')
                continue;
            var upper = char.ToUpperInvariant(c) switch
            {
                'O' => '0',
                'I' or 'L' => '1',
                var other => other
            };
            if (!Alphabet.Contains(upper) || code.Length == Length)
                return null;
            code.Append(upper);
        }
        return code.Length == Length ? code.ToString() : null;
    }

    /// <summary>Whether what was typed is the order's code, compared in constant time.</summary>
    public static bool Matches(byte[] key, Guid orderId, string? typed)
    {
        var normalized = Normalize(typed);
        if (normalized is null)
            return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(For(key, orderId)), Encoding.ASCII.GetBytes(normalized));
    }
}
