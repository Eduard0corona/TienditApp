using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;

namespace FoodParkDigital.Application.Tests.Support;

/// <summary>TOTP RFC 6238 (HMAC-SHA1, 30 s, 6 dígitos), como una app autenticadora.</summary>
internal static class Totp
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Compute(string base32Key, DateTimeOffset timestamp)
    {
        var counter = new byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(counter, timestamp.ToUnixTimeSeconds() / 30);
        var hash = HMACSHA1.HashData(FromBase32(base32Key), counter);

        var offset = hash[^1] & 0x0F;
        var binaryCode = ((hash[offset] & 0x7F) << 24)
            | (hash[offset + 1] << 16)
            | (hash[offset + 2] << 8)
            | hash[offset + 3];

        return (binaryCode % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    private static byte[] FromBase32(string input)
    {
        var output = new List<byte>();
        var buffer = 0;
        var bitsLeft = 0;

        foreach (var c in input.TrimEnd('=').ToUpperInvariant())
        {
            buffer = ((buffer << 5) | Base32Alphabet.IndexOf(c)) & 0xFFFF;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                output.Add((byte)(buffer >> (bitsLeft - 8)));
                bitsLeft -= 8;
            }
        }

        return output.ToArray();
    }
}
