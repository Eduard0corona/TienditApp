using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace FoodParkDigital.Domain.Assistants;

/// <summary>
/// The secrets of the authorization an AI assistant gets for a customer account (Fase 32E, ADR 0097): its codes and tokens
/// are 43 random characters, like the links of tracking, and only their SHA-256 is stored, so a copy of the tables opens no
/// account. The PKCE check (S256) lives here too.
/// </summary>
public static class AssistantSecrets
{
    /// <summary>32 random bytes in base64url, without padding.</summary>
    public const int Length = 43;

    public const int MinVerifierLength = 43;
    public const int MaxVerifierLength = 128;

    /// <summary>Whether <paramref name="value"/> has the shape of a code or a token; it says nothing of whether one exists.</summary>
    public static bool IsWellFormed(string? value)
        => value is { Length: Length } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>SHA-256 of the value, in hexadecimal: what is stored and looked up.</summary>
    public static string HashOf(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>A code_challenge of S256: the SHA-256 of a verifier in base64url, 43 characters.</summary>
    public static bool IsCodeChallenge(string? value) => IsWellFormed(value);

    /// <summary>A code_verifier of RFC 7636: 43 to 128 characters of its alphabet.</summary>
    public static bool IsCodeVerifier(string? value)
        => value is { Length: >= MinVerifierLength and <= MaxVerifierLength }
            && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~');

    /// <summary>The code_challenge of a verifier with S256.</summary>
    public static string ChallengeOf(string verifier) => Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    /// <summary>Whether <paramref name="verifier"/> is the one whose challenge is <paramref name="challenge"/>, in constant time.</summary>
    public static bool Verifies(string challenge, string? verifier)
        => IsCodeVerifier(verifier)
            && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(ChallengeOf(verifier!)), Encoding.ASCII.GetBytes(challenge));
}
